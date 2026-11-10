"""
UPM 로컬 즉시 최적화 트리거 (테스트 및 단독 사용용).
외부 서버나 MQTT 브로커 없이 UPM.Desktop의 로컬 HTTP 포트(8787)로 직접 최적화 요청을 보냅니다.
검은 창이 뜨지 않도록 .pyw 확장자로 실행됩니다.
"""
import sys
import urllib.request
import urllib.error

def main():
    url = "http://127.0.0.1:8787/optimize"
    req = urllib.request.Request(
        url,
        data=b"{}",
        headers={"Content-Type": "application/json"}
    )
    try:
        with urllib.request.urlopen(req, timeout=3) as resp:
            if resp.status == 200:
                sys.exit(0)
    except Exception:
        sys.exit(1)

if __name__ == "__main__":
    main()

