# OCR 接入与后续截图翻译

2026-11-11：以日常截图识别、划词和复制够用为目标。已接入本地轻量模型和截图翻译，暂不加入分块识别、多模型投票、复杂文档分析或离线翻译模型。

## 当前实现

- 固定 `Sdcb.SimdPaddleOCR` 2.0.0 和 `Sdcb.SimdPaddleOCR.Models.ChineseV6Small` 1.0.0，使用 CPU，无需额外原生推理运行库。
- 模型随程序附带，第一次点击 OCR 才在后台加载，后续复用；截图、贴图与 OCR 快捷键共用一个实例，串行处理请求以控制并发内存。
- 直接传入截图 BGRA 像素，中英文自动识别。极大图片限制长边 4096、总像素约 1600 万，再映射坐标回原图。
- 使用模型 CTC 对齐估算字符框，延续原图划词、全选、复制和 Esc 返回。字符框仍是估算值，旋转文字使用轴对齐外框。
- 首选项可切换 Windows OCR，系统识别语言仅作用于 Windows 引擎。增强引擎异常会记录日志并尝试系统引擎，成功返回空文本则直接显示未识别到文字。
- Windows OCR 备用路径仍需要系统语言包。截图不会上传，识别文本不写入应用日志。

上游源码已拉取到被忽略的 `.local/upstream/` 供核对。查看的上游提交是 `9b82a859c615f06e22d7f5cafdc35b4ba2f398ad`；实际运行的是上述固定 NuGet 版本，而非上游 main 的开发版。

## 小范围检查

运行 `.\scripts\test-ocr.ps1`。公开样例来自 [RapidOCR 测试目录](https://github.com/RapidAI/RapidOCR/tree/a538d3a75e26ca7ac521da8602842b9d6e5a872d/python/tests/test_files)，下载时校验 SHA-256。正确文本在执行识别前从图片及上游测试核对，固定在诊断代码中。原图只存本地，不进入源码提交和发布包。

| 样例 | 增强引擎结果 | 本机重复识别耗时 |
| --- | --- | --- |
| `text_rec.jpg`：韩国小馆 | 相符 | 31 ms |
| `en_rec.jpg`：英文句子 | 相符 | 51 ms |
| `short.png`：中国网络空间安全 | 相符 | 14 ms |
| `text_cls.jpg`：倒置的「怪我咯」 | 相符 | 16 ms |
| 本地绘制浅色中英界面文字 | 相符 | 60 ms |
| 本地绘制深色中英界面文字 | 相符 | 65 ms |
| 空白图 | 无误报 | 15 ms |

本次进程首次请求约 325 ms，含模型初始化、JIT 和识别。结果来自本机 Release CPU 运行，每张图两次；不代表其他硬件、真实复杂截图或大图的速度。文本比较使用 NFKC 并忽略空白，保留大小写和标点。原始文本及耗时见本地 `artifacts/ocr-smoke/ocr-smoke.json`。

同批 Windows OCR 在四张紧裁切公开图片上返回空文本；本地浅色/深色样例可识别，但将 `Ctrl+C`、`复制` 和版本号标点识别错。这些是少量便利样例，不能换算成通用准确率或推广为所有场景的引擎排名。另通过现有 123 项桌面回归，覆盖划词逻辑、空白图、大图坐标、扫码及原有编辑交互。

## 截图翻译

工具栏「译」按钮保持选中，并保留完整的标注工具栏。在下方增加与工具栏选项行等高的一行语言与翻译状态栏，外观沿用已有浅灰、蓝色强调和菜单渲染器。首次默认英语 → 中文简体，之后记住语言选择；支持自动检测、交换语言、单一眼睛图标切换原文/译文、复制文字、重试和 Esc 返回。切换语言不重复 OCR，当前截图会话最多缓存 80 个文字块/语言组合。

OCR 文本按完整检测行框组织，合并相邻且左边对齐的段落，每次最多两个文字块同时翻译，按原顺序组合结果。翻译期间完整工具栏仍可操作。译文在独立的透明层中，采样原文字区域周边的背景颜色覆盖字迹，再按区域大小自动缩放排版；标注画在译文层上方。复制截图、保存与贴图都包含当前显示的原文/译文层。切回原文或关闭翻译会恢复原始像素，改变选区会取消旧请求并重建覆盖层。

背景填充针对常见纯色界面，不进行图像修复；复杂背景、旋转文字及过长译文仍有显示限制。渲染使用 GDI+ 保留透明层的 alpha，避免 GDI 文字绘制造成字形像素透明、透出底层原文。

当前 `GooglePublicTranslationProvider` 通过 HTTPS 使用 `translate.googleapis.com/translate_a/single?client=gtx`，该公共网页接口也可在 [Translate Shell 上游实现](https://github.com/soimort/translate-shell/blob/develop/include/Translators/GoogleTranslate.awk)中核对。它不需要密钥，可能限流、变更或因网络不可达而失败，不能把可用性当成稳定承诺。它与需要凭据的 [Google Cloud Translation 官方 API](https://cloud.google.com/translate/docs/reference/rest/v2/translate)不同。

只提交用户点击翻译的选区 OCR 文字，不上传图片，也不记录原文、译文或带文字的请求 URL。请求有超时、取消、429 和网络错误提示，Esc 或新请求会取消旧请求并丢弃过期结果。单块超过 600 字符时按空白边界分段并保留换行；总长度限制 12000 字符、最多 80 块，过大时提示缩小选区。任一块请求失败会取消剩余任务并显示失败状态。本地 OCR 独立可用。

默认使用必应网页翻译。程序从必应翻译网页获取匿名会话，在会话有效期内复用，遇到限流时提示用户稍后重试。首选项也可选择 Google、MyMemory 或 Microsoft Azure。Azure 使用官方文本翻译 API，选择前需配置密钥，密钥按当前 Windows 用户加密保存。各服务通过 `ITranslationProvider` 接入截图翻译流程。[Microsoft Translator API](https://learn.microsoft.com/en-us/azure/ai-services/translator/text-translation/reference/v3/translate)

MyMemory 使用匿名查询接口，需要明确的源语言和目标语言。请求按换行及 500 个 UTF-8 字节上限分段，服务返回额度用尽时显示提示。[MyMemory API](https://mymemory.translated.net/doc/spec.php)
