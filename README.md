# TraderSync

## 기능

여러 플레이어가 같은 NPC 상인의 거래 창을 동시에 열어 구매와 판매를 할 수 있게 해 주는 **7 Days to Die** 모드입니다. 거래는 서버에서 순서대로 검증·처리되며, 변경된 상인 재고가 거래 창을 보고 있는 플레이어들에게 실시간으로 동기화됩니다. 자동판매기는 게임의 기존 거래 방식을 그대로 사용합니다.

## 레퍼런스

- Anthony Maltha가 제작한 기존 **TraderSync** 모드를 현재 게임 API에 맞게 포팅했습니다.
- 구현 시 **DrokkStorage** 모드의 구조와 동작을 참고했습니다. 관련 자료는 [`References/DrokkStorage`](References/DrokkStorage)에서 확인할 수 있습니다.
