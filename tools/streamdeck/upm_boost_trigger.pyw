"""
UPM 스트림덱(Ulanzi 등) 빠른 최적화 트리거.

라즈베리파이 MQTT 브로커의 'upm/control/boost' 토픽에 'EXECUTE'를 1회 발행하고 바로 종료합니다.
PC에서 실행 중인 UPM.Desktop이 이 토픽을 구독하고 있다가 빠른 최적화를 실행합니다.

- .pyw 확장자이므로 pythonw.exe로 실행되어 콘솔 창이 뜨지 않습니다.
- 성공 시 종료 코드 0, 실패 시 1 (오류 창 없이 조용히 종료).
- paho-mqtt 1.x / 2.x 모두 동작합니다.  설치: pip install paho-mqtt
"""
import sys

# [환경 설정] 라즈베리파이 IP 주소로 변경해 주십시오. (UPM 쪽 %LocalAppData%\UPM\mqtt.json 과 같아야 함)
BROKER_IP = "192.168.1.100"
PORT = 1883
TOPIC = "upm/control/boost"
PAYLOAD = "EXECUTE"
CLIENT_ID = "Ulanzi_Trigger_Btn"
TIMEOUT_SEC = 3

# 브로커에 인증을 설정했다면 입력 (예: {"username": "upm", "password": "..."}), 아니면 None
AUTH = None


def main() -> int:
    try:
        import socket
        import paho.mqtt.publish as publish
    except ImportError:
        return 1

    # 브로커가 꺼져 있을 때 오래 매달리지 않도록 연결 타임아웃 제한
    socket.setdefaulttimeout(TIMEOUT_SEC)

    try:
        # publish.single: 연결 → QoS1 발행(PUBACK 대기) → 연결 해제까지 한 번에 처리
        publish.single(
            TOPIC,
            payload=PAYLOAD,
            qos=1,
            hostname=BROKER_IP,
            port=PORT,
            client_id=CLIENT_ID,
            keepalive=10,
            auth=AUTH,
        )
        return 0
    except Exception:
        # 무창 실행 환경이므로 오류 창 없이 조용히 실패 처리
        return 1


if __name__ == "__main__":
    sys.exit(main())

