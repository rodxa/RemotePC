#include <WiFi.h>
#include <WiFiUdp.h>
#include <WiFiClientSecure.h>
#include <HTTPClient.h>
#include <ArduinoJson.h>
#include <ctype.h>

// Wi-Fi
const char* WIFI_SSID = "WIFI_SSID";
const char* WIFI_PASSWORD = "WIFI_PASSWORD";

const char* SUPABASE_URL =
  "https://YOUR_PROJECT.supabase.co";

const char* SUPABASE_KEY =
  "sb_publishable_KEY";

// Each physical ESP32 only needs this routing value changed.
const char* WAKE_AGENT = "home";

IPAddress WOL_BROADCAST(192, 168, 1, 255);

const uint16_t DEFAULT_WOL_PORT = 9;
const unsigned long POLL_INTERVAL_MS = 3000;
const size_t MAX_PCS = 64;
const uint8_t PRUNE_AFTER_MISSED_SCANS = 5;
const bool DEBUG_SUPABASE_JSON = false;

struct PcState {
  int64_t id = -1;
  int64_t lastObservedCommandId = -1;
  uint8_t missedScans = 0;
  bool active = false;
};

WiFiUDP udp;
PcState pcStates[MAX_PCS];

bool initialSyncComplete = false;
unsigned long lastPollMs = 0;

void connectWiFi() {
  if (WiFi.status() == WL_CONNECTED) {
    return;
  }

  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);

  Serial.print("Connecting to WiFi");

  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }

  Serial.println();
  Serial.print("WiFi connected: ");
  Serial.println(WiFi.localIP());
}

String urlEncode(const char* value) {
  String encoded;
  const char* hex = "0123456789ABCDEF";

  while (*value) {
    unsigned char c = static_cast<unsigned char>(*value++);
    if (isalnum(c) || c == '-' || c == '_' || c == '.' || c == '~') {
      encoded += static_cast<char>(c);
    } else {
      encoded += '%';
      encoded += hex[(c >> 4) & 0x0F];
      encoded += hex[c & 0x0F];
    }
  }

  return encoded;
}

int hexValue(char c) {
  if (c >= '0' && c <= '9') {
    return c - '0';
  }

  if (c >= 'a' && c <= 'f') {
    return c - 'a' + 10;
  }

  if (c >= 'A' && c <= 'F') {
    return c - 'A' + 10;
  }

  return -1;
}

bool parseMacAddress(const char* text, uint8_t mac[6]) {
  if (text == nullptr) {
    return false;
  }

  for (int octet = 0; octet < 6; octet++) {
    int high = hexValue(*text++);
    int low = hexValue(*text++);

    if (high < 0 || low < 0) {
      return false;
    }

    mac[octet] = static_cast<uint8_t>((high << 4) | low);

    if (octet < 5) {
      char separator = *text++;
      if (separator != ':' && separator != '-') {
        return false;
      }
    }
  }

  return *text == '\0';
}

void printMac(const uint8_t mac[6]) {
  for (int i = 0; i < 6; i++) {
    if (i > 0) {
      Serial.print(":");
    }

    if (mac[i] < 0x10) {
      Serial.print("0");
    }

    Serial.print(mac[i], HEX);
  }
}

void sendWakeOnLan(const uint8_t mac[6], uint16_t port) {
  uint8_t packet[102];

  for (int i = 0; i < 6; i++) {
    packet[i] = 0xFF;
  }

  for (int i = 1; i <= 16; i++) {
    for (int j = 0; j < 6; j++) {
      packet[i * 6 + j] = mac[j];
    }
  }

  Serial.println("Sending WoL...");

  udp.beginPacket(WOL_BROADCAST, port);
  udp.write(packet, sizeof(packet));
  udp.endPacket();

  Serial.println("Wake packet sent.");
}

PcState* findState(int64_t id) {
  for (size_t i = 0; i < MAX_PCS; i++) {
    if (pcStates[i].active && pcStates[i].id == id) {
      return &pcStates[i];
    }
  }

  return nullptr;
}

PcState* findFreeStateSlot() {
  for (size_t i = 0; i < MAX_PCS; i++) {
    if (!pcStates[i].active) {
      return &pcStates[i];
    }
  }

  return nullptr;
}

void markStatesMissing() {
  for (size_t i = 0; i < MAX_PCS; i++) {
    if (pcStates[i].active && pcStates[i].missedScans < 255) {
      pcStates[i].missedScans++;
    }
  }
}

void pruneMissingStates() {
  for (size_t i = 0; i < MAX_PCS; i++) {
    if (pcStates[i].active && pcStates[i].missedScans > PRUNE_AFTER_MISSED_SCANS) {
      Serial.print("Pruning missing PC state, id: ");
      Serial.println(static_cast<long>(pcStates[i].id));
      pcStates[i] = PcState();
    }
  }
}

void rememberPc(PcState* state, int64_t id, int64_t commandId) {
  state->id = id;
  state->lastObservedCommandId = commandId;
  state->missedScans = 0;
  state->active = true;
}

String buildSupabaseUrl() {
  return String(SUPABASE_URL) +
    "/rest/v1/pc_remote_control"
    "?wake_agent=eq." + urlEncode(WAKE_AGENT) +
    "&enabled=eq.true"
    "&mac_address=not.is.null"
    "&select=id,device_name,command_id,mac_address,wol_port";
}

bool fetchAndProcessPcs() {
  WiFiClientSecure client;

  // TODO: Production firmware should verify Supabase TLS certificates instead
  // of using setInsecure().
  client.setInsecure();

  HTTPClient http;
  http.setTimeout(10000);

  if (!http.begin(client, buildSupabaseUrl())) {
    Serial.println("Supabase HTTP begin failed.");
    return false;
  }

  http.addHeader("apikey", SUPABASE_KEY);
  http.addHeader("Accept", "application/json");

  int status = http.GET();

  if (status != 200) {
    Serial.print("Supabase HTTP error: ");
    Serial.println(status);
    Serial.println(http.getString());
    http.end();
    return false;
  }

  String body = http.getString();
  http.end();

  if (DEBUG_SUPABASE_JSON) {
    Serial.print("Supabase: ");
    Serial.println(body);
  }

  DynamicJsonDocument doc(12288);
  DeserializationError error = deserializeJson(doc, body);

  if (error) {
    Serial.print("Supabase JSON error: ");
    Serial.println(error.c_str());
    return false;
  }

  if (!doc.is<JsonArray>()) {
    Serial.println("Supabase JSON error: expected an array.");
    return false;
  }

  JsonArray pcs = doc.as<JsonArray>();

  Serial.print("Found ");
  Serial.print(pcs.size());
  Serial.println(" PCs");

  markStatesMissing();

  for (JsonObject pc : pcs) {
    int64_t id = pc["id"] | -1;
    int64_t commandId = pc["command_id"] | -1;
    const char* deviceName = pc["device_name"] | "(unnamed)";
    const char* macText = pc["mac_address"] | "";
    int wolPort = pc["wol_port"] | DEFAULT_WOL_PORT;

    if (id < 0 || commandId < 0) {
      Serial.print("Skipping invalid PC row: ");
      Serial.println(deviceName);
      continue;
    }

    if (wolPort < 1 || wolPort > 65535) {
      wolPort = DEFAULT_WOL_PORT;
    }

    uint8_t mac[6];
    if (!parseMacAddress(macText, mac)) {
      Serial.print("Invalid MAC for ");
      Serial.print(deviceName);
      Serial.print(": ");
      Serial.println(macText);
      continue;
    }

    PcState* state = findState(id);
    if (state == nullptr) {
      state = findFreeStateSlot();
      if (state == nullptr) {
        Serial.print("MAX_PCS reached; cannot track ");
        Serial.println(deviceName);
        continue;
      }

      rememberPc(state, id, commandId);

      Serial.print(initialSyncComplete ? "New PC synchronized: " : "Initial sync: ");
      Serial.print(deviceName);
      Serial.print(" -> command ");
      Serial.println(static_cast<long>(commandId));
      continue;
    }

    state->missedScans = 0;

    if (!initialSyncComplete) {
      state->lastObservedCommandId = commandId;
      continue;
    }

    if (commandId > state->lastObservedCommandId) {
      Serial.println("Wake command detected:");
      Serial.print("  PC: ");
      Serial.println(deviceName);
      Serial.print("  ID: ");
      Serial.println(static_cast<long>(id));
      Serial.print("  command: ");
      Serial.print(static_cast<long>(state->lastObservedCommandId));
      Serial.print(" -> ");
      Serial.println(static_cast<long>(commandId));
      Serial.print("  MAC: ");
      printMac(mac);
      Serial.println();

      sendWakeOnLan(mac, static_cast<uint16_t>(wolPort));
      state->lastObservedCommandId = commandId;
    } else if (commandId < state->lastObservedCommandId) {
      Serial.print("Command ID decreased for ");
      Serial.print(deviceName);
      Serial.print("; resyncing to ");
      Serial.println(static_cast<long>(commandId));
      state->lastObservedCommandId = commandId;
    }
  }

  pruneMissingStates();
  return true;
}

void setup() {
  Serial.begin(115200);

  connectWiFi();
  udp.begin(DEFAULT_WOL_PORT);

  delay(1000);

  Serial.print("Wake agent: ");
  Serial.println(WAKE_AGENT);
  Serial.println("Fetching assigned PCs...");

  while (!initialSyncComplete) {
    if (fetchAndProcessPcs()) {
      initialSyncComplete = true;
      Serial.println("Wake agent ready.");
      lastPollMs = millis();
    } else {
      Serial.println("Initial sync failed; retrying...");
      delay(POLL_INTERVAL_MS);
      connectWiFi();
    }
  }
}

void loop() {
  if (WiFi.status() != WL_CONNECTED) {
    connectWiFi();
  }

  unsigned long now = millis();
  if (now - lastPollMs >= POLL_INTERVAL_MS) {
    lastPollMs = now;
    fetchAndProcessPcs();
  }

  delay(50);
}
