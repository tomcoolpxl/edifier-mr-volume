# Third-party notices

Edifier MR Volume is MIT-licensed (see [LICENSE](LICENSE)). The release files also contain the
software below, under its own licence.

## MIT-licensed

Compiled into `EdifierMrVolume.exe`:

| Component | Copyright | Licence |
| --- | --- | --- |
| .NET runtime libraries, including System.Drawing.Common, Microsoft.Win32.SystemEvents and System.Numerics.Tensors | (c) .NET Foundation and Contributors | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |
| Windows Community Toolkit (CommunityToolkit.Common, CommunityToolkit.WinUI.Controls.SettingsControls, .Segmented, .Extensions, .Helpers, .Triggers) | (c) .NET Foundation and Contributors | [MIT](https://github.com/CommunityToolkit/Windows/blob/main/License.md) |
| H.NotifyIcon, H.NotifyIcon.WinUI, H.GeneratedIcons.System.Drawing | Copyright (c) 2020 havendv | [MIT](https://github.com/HavenDV/H.NotifyIcon/blob/master/LICENSE.md) |

The MIT licence for these components:

```text
Permission is hereby granted, free of charge, to any person obtaining a copy of this software
and associated documentation files (the "Software"), to deal in the Software without
restriction, including without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Microsoft licence terms

These files are next to the exe. Microsoft allows apps to ship them, under its own terms, which
also apply to you as a user of these files:

| Component | Files | Licence |
| --- | --- | --- |
| Windows App SDK 2.6.5 (WinUI 3, DWriteCore and the other runtime parts) | `Microsoft.*.dll`, `Microsoft.*.winmd`, `DWriteCore.dll`, `WinUIEdit.dll` and the other DLLs from the package | [Microsoft Software License Terms, Windows App SDK](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.6.5/License) |
| Windows AI Machine Learning 2.4.89, which brings DirectML and ONNX Runtime | `Microsoft.Windows.AI.MachineLearning.dll`, `DirectML.dll`, `onnxruntime.dll` | [Licence](https://www.nuget.org/packages/Microsoft.Windows.AI.MachineLearning/2.4.89/License) |
| WebView2 1.0.3719.77 | `Microsoft.Web.WebView2.Core.dll`, `WebView2Loader.dll` | [Licence](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.3719.77/License) |

## Protocol sources

The Bluetooth protocol details come from MIT-licensed reverse-engineering projects, listed under
Credits in the [README](README.md#credits).
