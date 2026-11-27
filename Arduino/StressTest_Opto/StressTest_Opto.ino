/*************************************************
 * StressTest_Opto
 * 光耦入口的壓力測試韌體（Arduino Nano V3.0）
 *
 * 序列協定（115200 baud，每行以 \n 結尾）
 *   Unity -> Arduino
 *     HELLO                        要求回報身分
 *     ONE,<START|UP|DOWN|STOP>     單發一次按壓
 *     ABORT                        中止執行中的 RUN（不在執行時回 ABORTED,0）
 *     ZERO                         逐筆計數歸零
 *     CMD,<seq>,<UP|DOWN>*XX       逐筆指令（A 送法），做完回 X
 *     STAT                         查詢逐筆計數
 *     RUN,<UP|DOWN>,<n>,<gap_ms>   連續按壓 n 次；每次按壓完成後休息 gap_ms
 *   Arduino -> Unity
 *     READY,OPTO,<版本>,<參數>     開機或回應 HELLO
 *     ACK,<原指令>                 已接受（ONE 在執行後、RUN 在開始前回覆）
 *     T,<i>,<start_us>,<end_us>    RUN 第 i 筆的開始／結束時間（整段結束後才送）
 *     DONE,<n_sent>                RUN 完成
 *     ABORTED,<n_sent>             RUN 被中止；n_sent 為中止前已完成的筆數
 *     ERR,<原因>                   格式錯誤、不支援或忙碌中
 *     X,<seq>,<rx_us>,<start_us>,<end_us>*XX   逐筆指令的完成回覆
 *     NAK,BAD_LINE*XX              逐筆指令毀損，沒有執行
 *     STAT,<收到>,<執行>,<毀損>*XX
 *   *XX 是 * 之前所有位元組 XOR 的兩位大寫十六進位。
 *
 * 單筆動作沿用正式版：腳位拉高 PRESS_MS 後拉低。
 * RUN 期間不做任何序列輸出，避免傳輸時間拉長間隔；
 * 期間收到的指令行在段結束後一律回 ERR,BUSY，不會被執行。
 *************************************************/

const char FIRMWARE_VERSION[] = "0.5.0";
const unsigned long BAUD_RATE = 115200;

// ===== 光耦腳位（與正式版相同） =====
const int PIN_UP    = 3;
const int PIN_DOWN  = 4;
const int PIN_STOP  = 5;
const int PIN_START = 6;

// ===== 單筆動作參數 =====
const unsigned long PRESS_MS = 120;

// ===== RUN 參數 =====
const int MAX_RUN_COUNT = 90;          // 時間戳存 RAM：90 × 8 bytes = 720 bytes
const unsigned long MAX_GAP_MS = 10000;
unsigned long runStart[MAX_RUN_COUNT];
unsigned long runEnd[MAX_RUN_COUNT];

// ===== 指令行緩衝 =====
const int LINE_CAPACITY = 48;
char lineBuffer[LINE_CAPACITY];
int lineLength = 0;
bool lineOverflow = false;

void releaseAll() {
  digitalWrite(PIN_UP, LOW);
  digitalWrite(PIN_DOWN, LOW);
  digitalWrite(PIN_STOP, LOW);
  digitalWrite(PIN_START, LOW);
}

void sendReady() {
  Serial.print(F("READY,OPTO,"));
  Serial.print(FIRMWARE_VERSION);
  Serial.print(F(",press_ms="));
  Serial.print(PRESS_MS);
  Serial.print(F(";action_ms="));
  Serial.print(PRESS_MS);
  Serial.print(F(";max_run="));
  Serial.print(MAX_RUN_COUNT);
  Serial.println(F(";ack=1"));
}

void press(int pin) {
  digitalWrite(pin, HIGH);
  delay(PRESS_MS);
  digitalWrite(pin, LOW);
}

// 將指令名稱對應到腳位；不認得回傳 -1
int pinForCommand(const char* name) {
  if (strcmp(name, "START") == 0) return PIN_START;
  if (strcmp(name, "UP") == 0)    return PIN_UP;
  if (strcmp(name, "DOWN") == 0)  return PIN_DOWN;
  if (strcmp(name, "STOP") == 0)  return PIN_STOP;
  return -1;
}

// RUN 期間收到的指令行：ABORT 立即中止；其他只計數，段結束後回 ERR,BUSY
int busyLines = 0;
char busyBuffer[8];
int busyLength = 0;
bool busyOverflow = false;
bool abortRequested = false;

void drainSerialWhileBusy() {
  while (Serial.available() > 0) {
    char c = Serial.read();
    if (c == '\r') continue;
    if (c == '\n') {
      busyBuffer[busyLength] = '\0';
      if (!busyOverflow && strcmp(busyBuffer, "ABORT") == 0) abortRequested = true;
      else busyLines++;
      busyLength = 0;
      busyOverflow = false;
    } else if (busyLength < (int)sizeof(busyBuffer) - 1) {
      busyBuffer[busyLength++] = c;
    } else {
      busyOverflow = true;
    }
  }
}

// 解析 "UP,20,5" 這類參數；成功回傳 true
bool parseRun(char* args, int* pin, int* count, unsigned long* gapMs) {
  char* dir = strtok(args, ",");
  char* countText = strtok(NULL, ",");
  char* gapText = strtok(NULL, ",");
  if (dir == NULL || countText == NULL || gapText == NULL || strtok(NULL, ",") != NULL) return false;

  if (strcmp(dir, "UP") == 0) *pin = PIN_UP;
  else if (strcmp(dir, "DOWN") == 0) *pin = PIN_DOWN;
  else return false;

  long n = atol(countText);
  long g = atol(gapText);
  if (n < 1 || n > MAX_RUN_COUNT) return false;
  if (g < 0 || (unsigned long)g > MAX_GAP_MS) return false;
  if (g == 0 && strcmp(gapText, "0") != 0) return false;

  *count = (int)n;
  *gapMs = (unsigned long)g;
  return true;
}

void runSegment(int pin, int count, unsigned long gapMs) {
  unsigned long gapUs = gapMs * 1000UL;

  abortRequested = false;
  busyLength = 0;
  busyOverflow = false;
  int sent = 0;

  for (int i = 0; i < count; i++) {
    drainSerialWhileBusy();
    if (abortRequested) break;  // 一筆動作完成後才檢查，不會切斷進行中的按壓／發射

    runStart[i] = micros();
    press(pin);
    runEnd[i] = micros();
    sent = i + 1;

    if (i < count - 1) {
      while (!abortRequested && micros() - runEnd[i] < gapUs) {
        drainSerialWhileBusy();
      }
    }
  }
  releaseAll();

  for (int i = 0; i < sent; i++) {
    Serial.print(F("T,"));
    Serial.print(i);
    Serial.print(',');
    Serial.print(runStart[i]);
    Serial.print(',');
    Serial.println(runEnd[i]);
  }
  if (abortRequested) {
    Serial.print(F("ABORTED,"));
    Serial.println(sent);
  } else {
    Serial.print(F("DONE,"));
    Serial.println(sent);
  }

  // RUN 期間收到的指令一律不執行
  for (; busyLines > 0; busyLines--) {
    Serial.println(F("ERR,BUSY"));
  }
  lineLength = 0;
  lineOverflow = false;
}

// ===== 逐筆指令（A 送法） =====
unsigned long cmdReceived = 0;         // 通過檢查碼的 CMD
unsigned long cmdExecuted = 0;         // 已做完動作的 CMD
unsigned long cmdBad = 0;              // 毀損、過長或不認得的行
const int REPLY_CAPACITY = 48;         // "X," + 4 個 32 位元數字

// 回覆行：內容後面接 *XX（內容所有位元組 XOR 的兩位大寫十六進位）
void printWithChecksum(const char* payload) {
  uint8_t x = 0;
  for (const char* p = payload; *p != '\0'; p++) x ^= (uint8_t)*p;
  Serial.print(payload);
  Serial.print('*');
  if (x < 16) Serial.print('0');
  Serial.println(x, HEX);
}

// 檢查碼相符時把 "*XX" 去掉並回傳 true
bool verifyChecksum(char* line) {
  char* star = strrchr(line, '*');
  if (star == NULL || strlen(star) != 3) return false;
  char* end;
  long expected = strtol(star + 1, &end, 16);
  if (*end != '\0') return false;
  uint8_t x = 0;
  for (char* p = line; p < star; p++) x ^= (uint8_t)*p;
  if (x != expected) return false;
  *star = '\0';
  return true;
}

void rejectCommand() {
  cmdBad++;
  printWithChecksum("NAK,BAD_LINE");
}

// CMD,<seq>,<UP|DOWN>*XX：執行一筆，做完才回 X,<seq>,<rx_us>,<start_us>,<end_us>*XX
void handleCommand(char* line, unsigned long rxUs) {
  if (!verifyChecksum(line)) {
    rejectCommand();
    return;
  }
  char* seqText = line + 4;
  char* comma = strchr(seqText, ',');
  if (comma == NULL) {
    rejectCommand();
    return;
  }
  *comma = '\0';
  const char* direction = comma + 1;
  int pin = pinForCommand(direction);
  if (pin != PIN_UP && pin != PIN_DOWN) {
    rejectCommand();
    return;
  }
  unsigned long seq = strtoul(seqText, NULL, 10);
  cmdReceived++;

  unsigned long startUs = micros();
  press(pin);
  unsigned long endUs = micros();
  cmdExecuted++;

  char reply[REPLY_CAPACITY];
  snprintf_P(reply, sizeof(reply), PSTR("X,%lu,%lu,%lu,%lu"), seq, rxUs, startUs, endUs);
  printWithChecksum(reply);
}

void handleLine(char* line, unsigned long rxUs) {
  if (strncmp(line, "CMD,", 4) == 0) {
    handleCommand(line, rxUs);
    return;
  }

  if (strcmp(line, "ZERO") == 0) {
    cmdReceived = cmdExecuted = cmdBad = 0;
    Serial.println(F("ACK,ZERO"));
    return;
  }

  if (strcmp(line, "STAT") == 0) {
    char reply[REPLY_CAPACITY];
    snprintf_P(reply, sizeof(reply), PSTR("STAT,%lu,%lu,%lu"), cmdReceived, cmdExecuted, cmdBad);
    printWithChecksum(reply);
    return;
  }

  if (strcmp(line, "HELLO") == 0) {
    sendReady();
    return;
  }

  if (strcmp(line, "ABORT") == 0) {
    // 沒有在執行 RUN：沒有東西可中止
    Serial.println(F("ABORTED,0"));
    return;
  }

  if (strncmp(line, "ONE,", 4) == 0) {
    int pin = pinForCommand(line + 4);
    if (pin < 0) {
      Serial.println(F("ERR,BAD_COMMAND"));
      return;
    }
    press(pin);
    Serial.print(F("ACK,"));
    Serial.println(line);
    return;
  }

  if (strncmp(line, "RUN,", 4) == 0) {
    // 先回 ACK（strtok 會改寫緩衝，所以在解析前輸出原指令）
    char original[LINE_CAPACITY];
    strncpy(original, line, LINE_CAPACITY);
    original[LINE_CAPACITY - 1] = '\0';

    int pin, count;
    unsigned long gapMs;
    if (!parseRun(line + 4, &pin, &count, &gapMs)) {
      Serial.println(F("ERR,BAD_RUN"));
      return;
    }
    Serial.print(F("ACK,"));
    Serial.println(original);
    Serial.flush();  // ACK 送完才開始計時，避免傳輸占用第一筆
    runSegment(pin, count, gapMs);
    return;
  }

  cmdBad++;
  Serial.println(F("ERR,UNKNOWN"));
}

void setup() {
  pinMode(PIN_UP, OUTPUT);
  pinMode(PIN_DOWN, OUTPUT);
  pinMode(PIN_STOP, OUTPUT);
  pinMode(PIN_START, OUTPUT);
  releaseAll();

  Serial.begin(BAUD_RATE);
  sendReady();
}

void loop() {
  while (Serial.available() > 0) {
    char c = Serial.read();

    if (c == '\r') continue;

    if (c == '\n') {
      unsigned long rxUs = micros();
      if (lineOverflow) {
        cmdBad++;
        Serial.println(F("ERR,LINE_TOO_LONG"));
      } else if (lineLength > 0) {
        lineBuffer[lineLength] = '\0';
        handleLine(lineBuffer, rxUs);
      }
      lineLength = 0;
      lineOverflow = false;
      continue;
    }

    if (lineLength < LINE_CAPACITY - 1) {
      lineBuffer[lineLength++] = c;
    } else {
      lineOverflow = true;
    }
  }
}
