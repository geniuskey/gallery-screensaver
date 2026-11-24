# GALLERY Screen Saver

A Windows screen saver for [GALLERY — The Art of Computation](https://gallery.euiyun.com). It opens the live exhibition in WebView2 and plays a shuffled selection of works. It can show one continuous work across all monitors.

## 설치와 설정

1. [GitHub Releases](https://github.com/geniuskey/gallery-screensaver/releases)에서 `Gallery.scr`을 받습니다. Windows 10/11, .NET Framework 4.8, Microsoft Edge WebView2 런타임이 필요합니다.
2. 파일을 우클릭하고 **설치**를 선택합니다.
3. **화면 보호기 설정 → 설정…**에서 작품당 시간, 작품 번호, 전환, 화질 등을 바꿀 수 있습니다. **전체 화면 미리 보기**는 저장 전에 현재 설정으로 시험합니다.

기본값은 **주 모니터만**이며, 보조 모니터는 검게 유지합니다. **여러 모니터에 이어서 표시**를 선택하면 Windows 화면 배치에 맞춰 작품 하나를 펼칩니다. 이 모드에서 화질이 **자동**이면 내부 렌더링 해상도를 낮추고 캡션은 주 모니터에 배치합니다.

작품은 설정에서 선택한 사이트의 라이브 전시에서 불러옵니다. 기본 사이트는 `https://gallery.euiyun.com`입니다. 사이트에 작품이 추가되면 다음 실행부터 전시 목록에 포함됩니다. 설정에서 작품 번호를 지정했다면 선택한 작품만 재생합니다.

화면 보호기를 제거하려면 Windows의 **화면 보호기 설정**에서 다른 화면 보호기 또는 **없음**을 선택한 뒤 `Gallery.scr`을 삭제합니다. 파일을 별도 위치에 복사했다면 해당 위치의 파일을 삭제합니다.

## 명령줄

| 인자 | 동작 |
|---|---|
| `/s` | 전체 화면 전시 |
| `/c` 또는 인자 없음 | 설정 대화상자 |
| `/p <hwnd>` | Windows 설정 창 안에 정적인 미리보기 표시 |
| `/selftest` | 단일 파일에서 WebView2 로더와 런타임 확인 |

Windows 사용자별 설정은 `HKCU\Software\Euiyun\Gallery Screen Saver`에 저장됩니다. WebView2 로더, 프로필, 마지막 실행 로그는 `%LOCALAPPDATA%\Euiyun\Gallery Screen Saver\`에 저장됩니다. `GALLERY_SAVER_DATA_DIR` 환경 변수로 테스트 데이터의 위치를 지정할 수 있습니다.

## 빌드와 테스트

Windows에서 .NET SDK로 실행합니다.

```powershell
dotnet build GallerySaver.csproj -c Release
./scripts/Test-Release.ps1
```

결과물은 `bin/Release/net48/Gallery.scr`입니다. 테스트는 별도 폴더에 파일 하나만 복사해 WebView2 런타임, 같은 프로세스에서 로더 재사용, Windows 설정 미리보기의 크기 변경과 종료를 확인합니다. [Windows 빌드 워크플로](.github/workflows/windows.yml)도 같은 테스트를 실행합니다.

## Code signing policy

현재 GitHub 릴리스의 서명 상태는 각 릴리스 안내에 표시합니다. 서명된 릴리스를 만들 때는 해당 태그의 빌드 결과물을 사용하고, Authenticode 서명과 RFC 3161 타임스탬프를 검증합니다. 서명 없는 파일에는 Windows SmartScreen 경고가 나타날 수 있습니다. [Microsoft SmartScreen 안내](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)

공개 코드 서명 인증서를 Windows 개인 인증서 저장소에서 사용할 수 있다면, [SignTool 기반 서명 스크립트](scripts/Sign-Release.ps1)로 서명된 사본을 만들 수 있습니다.

```powershell
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Select-Object Subject, Thumbprint, NotAfter
./scripts/Sign-Release.ps1 -ScreenSaver ./bin/Release/net48/Gallery.scr -CertificateThumbprint '<인증서 지문>'
```

서명 및 검증을 통과한 파일은 빌드 폴더의 `signed/Gallery.scr`에 생성됩니다.

### 개인정보

화면 보호기는 사용자가 설정한 전시 사이트에 접속하고, 그 페이지의 작품과 리소스를 불러옵니다. 화면 보호기 설정과 진단 로그는 로컬에 저장됩니다. 전시 사이트와 WebView2의 개인정보 처리는 각 서비스의 정책을 따릅니다. [GALLERY](https://gallery.euiyun.com) · [Microsoft 개인정보처리방침](https://privacy.microsoft.com/privacystatement)

## 라이선스

이 저장소의 화면 보호기 소스와 정적 미리보기 코드는 [MIT](LICENSE)입니다. [GALLERY 사이트와 작품](https://github.com/geniuskey/gallery)은 별도 저장소와 라이선스로 관리됩니다.

`Gallery.scr`에는 Microsoft WebView2 SDK의 .NET 어셈블리와 네이티브 로더가 들어 있습니다. Edge WebView2 런타임은 PC에 설치된 버전을 사용합니다. SDK 및 그 구성요소의 고지는 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)에 있습니다.
