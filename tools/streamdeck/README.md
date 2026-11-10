# 스트림덱 빠른 최적화 트리거

스트림덱 버튼 → 라즈베리파이 MQTT 브로커 → PC의 UPM.Desktop → 빠른 최적화 실행

```
[스트림덱] --upm_boost_trigger.pyw--> [라즈베리파이 Mosquitto :1883] --upm/control/boost--> [UPM.Desktop]
```

## 1. 라즈베리파이 (브로커)
```bash
sudo apt install -y mosquitto mosquitto-clients
# Mosquitto 2.x는 기본이 localhost 전용이므로 LAN 접속 허용
echo -e "listener 1883\nallow_anonymous true" | sudo tee /etc/mosquitto/conf.d/upm.conf
sudo systemctl restart mosquitto
```
> `allow_anonymous true`이면 같은 네트워크의 누구나 PC 최적화를 실행시킬 수 있습니다.
> 공유 네트워크에서는 `mosquitto_passwd`로 계정을 만들고, 아래 두 설정에 username/password를 넣으세요.

## 2. PC (UPM.Desktop)
기본 브로커 주소는 `192.168.1.100:1883`입니다. 다르면 `%LocalAppData%\UPM\mqtt.json`을 만들고 UPM을 다시 시작하세요.
```json
{
  "Enabled": true,
  "BrokerHost": "192.168.1.100",
  "BrokerPort": 1883,
  "Topic": "upm/control/boost",
  "Payload": "EXECUTE",
  "Username": null,
  "Password": null
}
```
- 브로커가 꺼져 있어도 UPM은 정상 동작하며, 10초마다 재연결을 시도합니다.
- 버튼을 연타해도 실행 중이거나 직전 실행 후 5초 이내의 신호는 무시됩니다.
- 완료되면 MessageBox 대신 "빠른 최적화" 버튼 라벨에 "N개 정리됨"이 3초간 표시됩니다 (게임 포커스 보호).

## 3. 스트림덱 스크립트
```powershell
pip install -r tools\streamdeck\requirements.txt
```
1. `upm_boost_trigger.pyw`의 `BROKER_IP`를 라즈베리파이 IP로 수정합니다.
2. 스트림덱(Ulanzi Studio 등)에서 버튼에 **프로그램 열기 / Open** 동작을 추가하고 대상에 아래를 지정합니다.
   ```
   pythonw.exe "C:\Dev\upm-client\tools\streamdeck\upm_boost_trigger.pyw"
   ```
   (`.pyw`가 pythonw에 연결되어 있으면 스크립트 경로만 지정해도 됩니다.)

## 동작 확인
```bash
# 라즈베리파이에서 수신 확인
mosquitto_sub -h localhost -t upm/control/boost -v
# 스크립트 없이 직접 트리거
mosquitto_pub -h 192.168.1.100 -t upm/control/boost -m EXECUTE -q 1
```

