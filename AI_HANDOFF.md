# OneToOneMessenger — AI 작업 인수인계

최종 갱신: 2026-09-26  
대상 작업공간: `C:\Users\jcy03\dev\OneToOneMessenger`  
기준 문서: `1to1_메신저_개발_마스터플랜.md`

## 다음 AI에게 먼저 전달할 내용

사용자가 기능 개발을 잠시 멈추고 현재 클라이언트 UI 개선을 우선 요청했다. 기능 잔여 항목들은 중단된 것이 아니라 **사용자 요청에 따라 대기 중**이다. 이후 사용자가 재개를 명확히 요청하기 전까지 기능 백로그를 임의로 진행하지 않는다.

최근 화면에서 파일 메시지 말풍선이 불필요하게 크게 비고 액션 버튼이 어색하게 배치되는 문제가 제기됐다. 다음 변경을 적용했지만 **실제 실행 화면을 다시 캡처해 검증하지는 못했다**. 첫 우선순위는 최신 빌드를 실행해 결과 화면을 확인하고, 문제가 남으면 XAML 항목 측정/정렬을 추가 조정하는 것이다.

## 프로젝트 요약

- Windows 1:1 메신저. WinUI 3 클라이언트와 ASP.NET Core 서버가 SignalR/REST로 통신한다.
- 기술: .NET 10, WinUI 3, ASP.NET Core, SignalR, SQLite(WAL/FTS5), 파일 시스템 저장소.
- 기본 로컬 서버 주소: `http://localhost:5000`.
- 클라이언트 화면의 현재 사용자 이름은 `철수`, 상대 표시 이름은 `짱구`로 고정되어 있다.
- 이 작업환경은 Git 저장소로 초기화되어 있지 않다고 보고됐다. 커밋이나 Git diff를 전제하지 말 것.
- 개발 마스터플랜은 기능 요구사항과 설계의 기준 문서다. 단, 일부 Phase 체크박스/설명은 과거 상태가 남아 있으므로 이 인수인계의 “현재 상태”를 우선 참고하고, 불일치는 코드와 최신 테스트를 확인해 정리한다.

## 구현된 범위

### 서버 및 공용 계약

- 공용 계약: `src\Shared\Class1.cs`
  - `MsgType`, `MessageDto`, `FileDto`, `VaultSummaryDto`, `VaultFileDto`, `SearchResultDto`
- 서버 진입점: `src\Server\Program.cs`
- DB 초기화와 스키마: `src\Server\Data\DatabaseInitializer.cs`
  - SQLite, WAL, foreign keys, 메시지/파일 테이블, 인덱스, FTS5 trigram 및 트리거
- 저장소:
  - `src\Server\Services\MessageStore.cs`
  - `src\Server\Services\FileStore.cs`
  - `src\Server\Services\FileCategoryService.cs`
- SignalR Hub: `src\Server\Hubs\ChatHub.cs`
  - 접속 사용자, 실시간 메시지, 읽음 상태, 상대 Presence
- REST:
  - `GET /api/health`
  - `GET /api/messages?beforeSeq=&limit=`
  - `GET /api/messages/search?q=&limit=`
  - `POST /api/files/upload?name=&sender=&body=` — raw stream body
  - `GET /api/files/{id}/download`
  - `GET /api/vault?category=&q=&sort=&offset=&limit=`
  - `GET /api/vault/summary`

### 클라이언트

- `src\Client\MainPage.xaml` / `MainPage.xaml.cs`
  - 채팅 및 보관함 기본 2열 UI
  - 텍스트 메시지 송수신, 초기 히스토리, 읽음 표시/요청, Presence
  - 파일 선택/드래그앤드롭 업로드, 진행률, 실패 재시도, 다운로드
  - 이미지 미리보기/확대
  - 검색 오버레이, FTS5 검색, Ctrl+F/Esc, 검색 결과 원문 이동
  - 보관함 카테고리 필터/정렬/파일명 검색/summary 및 “채팅에서 보기”
  - 설정: 알림 on/off, 다운로드 경로 지정 및 저장
- 서비스:
  - `src\Client\Services\ChatApiService.cs`
  - `src\Client\Services\ChatHubService.cs`
  - `src\Client\Services\ClientSettingsService.cs`
  - `src\Client\Services\NotificationService.cs`
- 트레이:
  - `src\Client\MainWindow.xaml` / `MainWindow.xaml.cs`
  - NuGet `H.NotifyIcon.WinUI` 2.4.1
  - 창 X 시 숨김, 더블클릭/열기 복원, 종료 메뉴
- 알림:
  - 새 메시지 및 다운로드 완료 toast. 새 메시지 알림은 설정값으로 on/off
- 최근 UI 개선:
  - 일반 파일에서 이미지 전용 미리보기 영역이 불필요한 높이를 차지하지 않도록 표시 조건 조정
  - 텍스트가 없는 파일 메시지의 빈 본문 숨김
  - 파일 액션은 한 행에 파일명/다운로드/다른 이름 저장으로 정리
  - 이미지 미리보기 고정 높이 제거, 최대 크기 제한과 실패 시 안내
  - ListView 항목의 상단 정렬 및 선택 배경 제거
  - 위 수정의 XAML/C# 빌드는 통과했지만 현재 화면에서의 시각적 확인은 대기

## 현재 검증 상태

마지막 변경 뒤 실행 결과:

```powershell
dotnet build .\OneToOneMessenger.slnx --no-restore --verbosity:minimal
dotnet test .\src\Server.Tests\Server.Tests.csproj --no-restore --verbosity:minimal
```

- 빌드: 성공, 경고 0 / 오류 0
- 서버 테스트: 6개 통과
- 별도 Windows UI 실기기 인수 테스트는 완료된 것이 아니다.

## 사용자가 대기시킨 잔여 제품 작업

다음 항목들은 알려진 미완료/부분 완료 작업이다. 사용자 요청으로 대기 중이며, UI 개선을 마치기 전 먼저 착수하지 않는다.

- 아바타
- 날짜 구분 및 발신자/메시지 그룹핑
- 텍스트 optimistic UI와 실패/재시도 표시
- 읽음 처리를 메시지 가시성과 연동하고 상태 정확도를 강화
- 오래된 히스토리 무한 스크롤 및 스크롤 위치 보존
- 보관함 목록의 추가 페이징, 이미지 그리드, 검색 디바운스
- 창 폭이 좁을 때 보관함 패널 오버레이 전환 및 SplitView 토글
- UI 상세 디자인/시간 포맷 정책
- Linux `systemd` 배포, WireGuard 설정
- 최종 18개 수용 테스트 및 2대 PC 통합 테스트

## 권장 재개 순서

1. 사용자에게서 받은 최신 UI 화면을 최신 클라이언트 빌드로 재현한다. 서버가 필요하면 먼저 `src\Server` 실행 후 클라이언트를 실행한다.
2. 빈 파일 말풍선의 실제 크기와 파일/텍스트 말풍선의 정렬을 확인한다. 문제가 남으면 `MainPage.xaml`의 ListViewItem/DataTemplate 측정 제약부터 확인한다.
3. 일반 텍스트, 이미지, 문서/기타 파일, caption이 있는 파일 메시지, 보관함 원문 이동을 각각 확인한다. 특히 null `File`/`Body`에서 조건부 Visibility가 올바른지 확인한다.
4. 필요하면 사용자에게 화면 캡처를 받아 한 번에 여러 UI 불편 요소를 우선순위화한다. 사용자가 “기능 작업 재개”를 명시하기 전에는 위 대기 백로그를 건드리지 않는다.
5. UI 변경마다 위 빌드와 서버 테스트를 재실행하고, 가능하면 실제 실행 화면을 재검증한다.

## 주의/알려진 한계

- 서버는 현재 `Program.cs`에서 `http://localhost:5000`에 바인딩한다. 원격/배포 연결 전에 환경설정/보안 구성을 별도로 점검해야 한다.
- 검색 결과의 `HighlightedPreview`는 서버가 `<mark>` 태그를 포함한 문자열로 반환하지만, 클라이언트는 현재 일반 텍스트로 표시한다.
- `GetMessagesAsync`는 서버 페이지 조회를 사용하며, 초기 로드만으로 무한 스크롤 기능이 완성된 것은 아니다.
- Toast/트레이/UI 동작은 빌드만으로 검증되지 않는다. Windows에서 실행 확인이 필요하다.
- `1to1_메신저_개발_마스터플랜.md`의 Phase 설명과 체크박스 일부에는 과거의 미구현 설명이 남아 있다. 코드/테스트를 확인하지 않고 체크 상태만 신뢰하지 말 것.

## 주요 파일 빠른 링크

- `1to1_메신저_개발_마스터플랜.md` — 전체 요구사항/아키텍처/수용 기준
- `src\Client\MainPage.xaml` — 채팅/보관함 UI
- `src\Client\MainPage.xaml.cs` — UI 이벤트 및 앱 동작
- `src\Client\MainWindow.xaml` — 타이틀바/트레이 아이콘
- `src\Client\Services\` — REST, SignalR, 로컬 설정, 알림
- `src\Server\Program.cs` — HTTP/SignalR 라우트 구성
- `src\Server\Services\MessageStore.cs` — 메시지/검색/보관함 DB 쿼리
- `src\Server.Tests\` — 서버 단위/통합 테스트

