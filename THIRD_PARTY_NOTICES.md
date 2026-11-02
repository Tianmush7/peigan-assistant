# Third-party notices

The MIT license in the repository root covers the project's own code. It does not replace third-party license terms.

| Component | Included version | License / notices |
| --- | --- | --- |
| Bilibili-thread-ripper | 2026.10.8.1; commit `0302b345cdfe367ac599b52dc6874e3f7967e220` | MIT; `src/YingBan.App/Assets/vendor/btr/LICENSE.md` (portable build: `Assets/vendor/btr/LICENSE.md`) |
| Microsoft.Web.WebView2 SDK / loader | 1.0.4258.31 | `licenses/WebView2-LICENSE.txt`, `licenses/WebView2-NOTICE.txt` |
| .NET runtime | 10.0.0 in the initial release | `licenses/dotnet-runtime-LICENSE.txt`, `licenses/dotnet-runtime-NOTICES.txt` |
| .NET Windows Desktop runtime | 10.0.0 in the initial release | `licenses/windowsdesktop-LICENSE.txt`, `licenses/windowsdesktop-NOTICES.txt` |

The bundled Bilibili userscript is unmodified. Its SHA-256 is `a21f17028406df43bc71a3af17e07a2e490582480a0db275f693ce624501b97a`. See its adjacent README for integration details. It is updated with the application, not downloaded and executed automatically.

The WebView2 browser runtime itself is not bundled. Users obtain it from Microsoft. The portable archive includes the application's SDK/loader libraries and the self-contained .NET runtime.

When updating dependencies or runtime versions, review and refresh the matching license files. The packaging script checks the versions associated with these bundled notices.
