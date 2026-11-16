# 로컬 GPU 수집 API 규격

클라이언트는 loopback HTTP만 허용합니다. 기본 예시는 `http://127.0.0.1:4317`입니다. 인증정보·경로·쿼리·fragment가 포함된 base URL과 원격 호스트는 거부하며 HTTP 리다이렉트를 따르지 않습니다.

## 상태 조회

`GET /api/state`는 아래 형식의 JSON을 반환합니다. [가상 응답 예제](../examples/state.json)를 참고하세요. 예제 장비와 UUID는 실제 장비 정보가 아닙니다.

| 필드 | 처리 |
| --- | --- |
| updatedAt | ISO 8601 수집 시각. 이 값이 없으면 이력 저장하지 않음 |
| refreshing | true이면 수집 중이며 이력 저장하지 않음 |
| servers | 서버 배열. 누락되거나 형식이 다르면 오류 |
| servers[].id / hostname / role | 서버 키·표시 이름·용도 |
| servers[].status | live / partial / offline. offline이면 현재 측정값을 사용하지 않음 |
| servers[].errors | 수집 오류 문자열 배열 |
| gpus[].index / model / liveModel / uuid | GPU 표시와 구분. liveModel이 있으면 우선 표시 |
| gpus[].totalMiB / usedMiB | 현재 수집 용량. 누락·음수·비수치 값은 미측정 |
| gpus[].utilization | 연산 사용률. 0~100, VRAM 점유와 별개 |
| gpus[].source | 수집 근거 표시 |
| gpus[].processes | pid / owner / container / usedMiB / models |

MiB 값은 화면에서 1024로 나누어 GiB로 표시합니다. `memoryPercent`나 과거 스냅샷 필드를 현재 측정값 대신 사용하지 않습니다. 프로세스 메모리는 합계이며 공유 모델 수로 나누지 않습니다.

조회는 3초 간격이고 동시에 중복 요청하지 않습니다. 상태 응답 크기는 최대 4MiB이며 요청/본문 읽기에 시간 제한이 있습니다. 오류 응답을 정상 빈 상태로 처리하지 않습니다.

## 새 수집 요청

`POST /api/refresh`와 아래 헤더를 사용합니다.

```http
X-GPU-View: 1
```

사용자가 `현황 갱신`을 눌렀을 때만 보냅니다. API는 성공 HTTP 상태로 수집 접수를 알리고 진행 중에는 상태의 `refreshing`을 true로 반환할 수 있습니다. 완료 상태는 이후 GET에서 읽습니다.

API 서버 구현과 GPU 측정 방식은 연동 대상의 책임입니다. 클라이언트가 원격 명령을 실행하거나 SSH 계정을 저장하지 않습니다.
