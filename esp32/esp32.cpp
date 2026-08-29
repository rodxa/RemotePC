#include <WiFi.h>
#include <WiFiUdp.h>
#include <WiFiClientSecure.h>
#include <HTTPClient.h>

// Wi-Fi
const char* WIFI_SSID = "WIFI_SSID";
const char* WIFI_PASSWORD = "WIFI_PASSWORD";

const char* SUPABASE_URL =
  "https://YOUR_PROJECT.supabase.co";

const char* SUPABASE_KEY =
  "sb_publishable_KEY";

uint8_t pcMac[6] = {
  0x9C, 0x6B, 0x00, 0x7B, 0xDC, 0x44
};

WiFiUDP udp;

long lastCommandId = -1;

void connectWiFi() {
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);

  Serial.print("Connecting");

  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }

  Serial.println();
  Serial.print("Connected: ");
  Serial.println(WiFi.localIP());
}

void sendWakeOnLan() {
  uint8_t packet[102];

  for (int i = 0; i < 6; i++) {
    packet[i] = 0xFF;
  }

  for (int i = 1; i <= 16; i++) {
    for (int j = 0; j < 6; j++) {
      packet[i * 6 + j] = pcMac[j];
    }
  }

  IPAddress broadcastIp(192, 168, 1, 255);

  udp.beginPacket(broadcastIp, 9);
  udp.write(packet, sizeof(packet));
  udp.endPacket();

  Serial.println("WAKE PACKET SENT!");
}

long getCommandId() {
  WiFiClientSecure client;

  // Fine for our first test.
  // We'll improve certificate verification afterwards.
  client.setInsecure();

  HTTPClient http;

  String url =
    String(SUPABASE_URL) +
    "/rest/v1/pc_remote_control"
    "?device_name=eq.home-pc"
    "&select=command_id";

  http.begin(client, url);

  // IMPORTANT:
  // Publishable key goes in apikey.
  http.addHeader("apikey", SUPABASE_KEY);

  int status = http.GET();

  if (status != 200) {
    Serial.print("Supabase HTTP error: ");
    Serial.println(status);

    String error = http.getString();
    Serial.println(error);

    http.end();

    return -1;
  }

  String body = http.getString();

  Serial.print("Supabase: ");
  Serial.println(body);

  http.end();

  // Expected:
  // [{"command_id":3}]

  int colon = body.indexOf(':');
  int end = body.indexOf('}');

  if (colon == -1 || end == -1) {
    return -1;
  }

  String value = body.substring(colon + 1, end);

  return value.toInt();
}

void setup() {
  Serial.begin(115200);

  connectWiFi();

  udp.begin(9);

  delay(1000);

  // IMPORTANT:
  // On startup we remember the current command
  // instead of immediately executing it.
  lastCommandId = getCommandId();

  Serial.print("Initial command ID: ");
  Serial.println(lastCommandId);
}

void loop() {

  if (WiFi.status() != WL_CONNECTED) {
    connectWiFi();
  }

  long currentCommandId = getCommandId();

  if (
    currentCommandId >= 0 &&
    lastCommandId >= 0 &&
    currentCommandId > lastCommandId
  ) {

    Serial.println("NEW WAKE COMMAND!");

    sendWakeOnLan();

    lastCommandId = currentCommandId;
  }

  delay(3000);
}