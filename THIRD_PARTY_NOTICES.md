# Third-party notices

SnipLite's original source code is licensed under the MIT License. Dependencies retain their own licenses.

## SimdPaddleOCR and embedded PP-OCRv6 Small models

| Package | Version | License |
| --- | --- | --- |
| Sdcb.SimdPaddleOCR | 2.0.0 | Apache-2.0 |
| Sdcb.SimdPaddleOCR.ModelProvider | 1.0.0 | Apache-2.0 |
| Sdcb.SimdPaddleOCR.Models.ChineseV6Small | 1.0.0 | Apache-2.0 |
| Sdcb.SimdPaddleOCR.Models.TextLineOrientation | 1.0.0 | Apache-2.0 |

Copyright (c) 2026 Sdcb.PaddleOCR contributors. The bundled PP-OCRv6 DET/REC, text-line orientation CLS and dictionary originate from the PaddleOCR ecosystem: Copyright (c) 2016 PaddlePaddle Authors. All Rights Reserved.

- Upstream: https://github.com/sdcb/SimdPaddleOCR and https://github.com/PaddlePaddle/PaddleOCR
- License: [Apache License 2.0](licenses/SimdPaddleOCR-Apache-2.0.txt)
- Original model attribution and sources: [upstream third-party notices](licenses/SimdPaddleOCR-THIRD-PARTY-NOTICES.txt)

SnipLite uses the CPU inference path. The models are embedded in their package assemblies; no external model service or API key is required. PaddleOCR and PP-OCR names belong to their respective owners; SnipLite is not endorsed by them.

## ZXing.Net and Windows Compatibility bindings

| Package | Version | License |
| --- | --- | --- |
| ZXing.Net | 0.16.11 | Apache-2.0 |
| ZXing.Net.Bindings.Windows.Compatibility | 0.16.17 | Apache-2.0 |

Maintained by Michael Jahn and contributors. ZXing.Net is a port of the ZXing barcode library.

- Upstream: https://github.com/micjahn/ZXing.Net
- License text: [Apache License 2.0](licenses/ZXing-Apache-2.0.txt)

## .NET and Windows components

SnipLite uses the .NET runtime, Windows desktop framework, Windows OCR, and Windows accessibility APIs. Windows system components are provided by the operating system and are not included in this source repository.

.NET license and third-party notices are included under `licenses/dotnet/`. A self-contained publish also includes .NET runtime binaries; their original licensing terms continue to apply.

- .NET: https://github.com/dotnet/runtime
- Windows desktop framework: https://github.com/dotnet/wpf and https://github.com/dotnet/winforms

## Windows SDK .NET projections

`Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll` are supplied by the implicit `Microsoft.Windows.SDK.NET.Ref` targeting pack (10.0.19041.57 in the verified build). The package is provided by Microsoft and references the [Windows SDK license](https://aka.ms/WinSDKLicenseURL). Windows Runtime interop is developed in [CsWinRT](https://github.com/microsoft/CsWinRT).

These components retain their original terms; SnipLite's MIT license applies to this project's original source code.

## Windows installer

The setup executable is built with Inno Setup 6.7.4, copyright Jordan Russell and Martijn Laan. The compiler is a build tool and is not included in this source repository. Inno Setup's original installer notices are preserved.

- Upstream: https://jrsoftware.org/
- License: [Inno Setup License](licenses/Inno-Setup-LICENSE.txt)
