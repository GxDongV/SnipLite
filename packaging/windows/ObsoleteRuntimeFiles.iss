; Exact obsolete dependencies from the v0.3.13/v0.3.14 payload audits.
; Only remove a named file when the new publish directory no longer ships it.
; Never use DLL wildcards: future SDK versions can reintroduce dependencies.
#if !FileExists(PublishDir + "\Microsoft.Windows.SDK.NET.dll")
Type: files; Name: "{app}\Microsoft.Windows.SDK.NET.dll"
#endif
#if !FileExists(PublishDir + "\WinRT.Runtime.dll")
Type: files; Name: "{app}\WinRT.Runtime.dll"
#endif
#if !FileExists(PublishDir + "\System.Windows.Forms.Design.dll")
Type: files; Name: "{app}\System.Windows.Forms.Design.dll"
#endif
#if !FileExists(PublishDir + "\System.Windows.Forms.Design.Editors.dll")
Type: files; Name: "{app}\System.Windows.Forms.Design.Editors.dll"
#endif
#if !FileExists(PublishDir + "\D3DCompiler_47_cor3.dll")
Type: files; Name: "{app}\D3DCompiler_47_cor3.dll"
#endif
#if !FileExists(PublishDir + "\DirectWriteForwarder.dll")
Type: files; Name: "{app}\DirectWriteForwarder.dll"
#endif
#if !FileExists(PublishDir + "\PenImc_cor3.dll")
Type: files; Name: "{app}\PenImc_cor3.dll"
#endif
#if !FileExists(PublishDir + "\PresentationCore.dll")
Type: files; Name: "{app}\PresentationCore.dll"
#endif
#if !FileExists(PublishDir + "\PresentationFramework-SystemCore.dll")
Type: files; Name: "{app}\PresentationFramework-SystemCore.dll"
#endif
#if !FileExists(PublishDir + "\PresentationFramework-SystemData.dll")
Type: files; Name: "{app}\PresentationFramework-SystemData.dll"
#endif
#if !FileExists(PublishDir + "\PresentationFramework-SystemDrawing.dll")
Type: files; Name: "{app}\PresentationFramework-SystemDrawing.dll"
#endif
#if !FileExists(PublishDir + "\PresentationFramework-SystemXml.dll")
Type: files; Name: "{app}\PresentationFramework-SystemXml.dll"
#endif
#if !FileExists(PublishDir + "\PresentationFramework-SystemXmlLinq.dll")
Type: files; Name: "{app}\PresentationFramework-SystemXmlLinq.dll"
#endif
#if !FileExists(PublishDir + "\PresentationFramework.Aero.dll")
Type: files; Name: "{app}\PresentationFramework.Aero.dll"
#endif
#if !FileExists(PublishDir + "\PresentationFramework.Aero2.dll")
Type: files; Name: "{app}\PresentationFramework.Aero2.dll"
#endif
#if !FileExists(PublishDir + "\PresentationFramework.AeroLite.dll")
Type: files; Name: "{app}\PresentationFramework.AeroLite.dll"
#endif
#if !FileExists(PublishDir + "\PresentationFramework.Classic.dll")
Type: files; Name: "{app}\PresentationFramework.Classic.dll"
#endif
#if !FileExists(PublishDir + "\PresentationFramework.dll")
Type: files; Name: "{app}\PresentationFramework.dll"
#endif
#if !FileExists(PublishDir + "\PresentationFramework.Fluent.dll")
Type: files; Name: "{app}\PresentationFramework.Fluent.dll"
#endif
#if !FileExists(PublishDir + "\PresentationFramework.Luna.dll")
Type: files; Name: "{app}\PresentationFramework.Luna.dll"
#endif
#if !FileExists(PublishDir + "\PresentationFramework.Royale.dll")
Type: files; Name: "{app}\PresentationFramework.Royale.dll"
#endif
#if !FileExists(PublishDir + "\PresentationNative_cor3.dll")
Type: files; Name: "{app}\PresentationNative_cor3.dll"
#endif
#if !FileExists(PublishDir + "\PresentationUI.dll")
Type: files; Name: "{app}\PresentationUI.dll"
#endif
#if !FileExists(PublishDir + "\ReachFramework.dll")
Type: files; Name: "{app}\ReachFramework.dll"
#endif
#if !FileExists(PublishDir + "\System.Printing.dll")
Type: files; Name: "{app}\System.Printing.dll"
#endif
#if !FileExists(PublishDir + "\System.Windows.Controls.Ribbon.dll")
Type: files; Name: "{app}\System.Windows.Controls.Ribbon.dll"
#endif
#if !FileExists(PublishDir + "\System.Windows.Input.Manipulations.dll")
Type: files; Name: "{app}\System.Windows.Input.Manipulations.dll"
#endif
#if !FileExists(PublishDir + "\System.Windows.Presentation.dll")
Type: files; Name: "{app}\System.Windows.Presentation.dll"
#endif
#if !FileExists(PublishDir + "\System.Windows.Primitives.dll")
Type: files; Name: "{app}\System.Windows.Primitives.dll"
#endif
#if !FileExists(PublishDir + "\System.Xaml.dll")
Type: files; Name: "{app}\System.Xaml.dll"
#endif
#if !FileExists(PublishDir + "\UIAutomationClient.dll")
Type: files; Name: "{app}\UIAutomationClient.dll"
#endif
#if !FileExists(PublishDir + "\UIAutomationClientSideProviders.dll")
Type: files; Name: "{app}\UIAutomationClientSideProviders.dll"
#endif
#if !FileExists(PublishDir + "\UIAutomationProvider.dll")
Type: files; Name: "{app}\UIAutomationProvider.dll"
#endif
#if !FileExists(PublishDir + "\UIAutomationTypes.dll")
Type: files; Name: "{app}\UIAutomationTypes.dll"
#endif
#if !FileExists(PublishDir + "\vcruntime140_cor3.dll")
Type: files; Name: "{app}\vcruntime140_cor3.dll"
#endif
#if !FileExists(PublishDir + "\WindowsFormsIntegration.dll")
Type: files; Name: "{app}\WindowsFormsIntegration.dll"
#endif
#if !FileExists(PublishDir + "\wpfgfx_cor3.dll")
Type: files; Name: "{app}\wpfgfx_cor3.dll"
#endif
