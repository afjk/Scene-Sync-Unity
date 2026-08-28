# Scene Sync Unity XRクライアント

主要なXRヘッドセットでSceneSyncコンテンツを受信するための、最小構成のUnityクライアントです。

## 基本構成

- Unity `6000.3.20f1`
- STYLY XR Rig `0.4.22`（commit `737d02a3741cdef9fe1f007bb3e0019911ce0b40` に固定）
- Scene Sync `0.26.3`
- Universal Render Pipeline `17.3.0`
- PolySpatial visionOS `3.2.2`
- Unity CLI Loop `2.1.10`（開発・動作確認用）

初期シーンは `Assets/SceneSyncClient/Scenes/SceneSyncClient.unity` です。
再生するとSceneSyncへ自動接続します。`SceneSyncClientController` のRoomが空の場合は、
Presence Serverが送信元IPから割り当てるLAN内のRoomへ接続します。特定のRoomへ接続する場合だけ、
Roomへコードを設定してください。

シーンを再生成する場合は、Unity Editorで次のメニューを実行します。

`Tools > Scene Sync XR Client > Create Minimal Project Setup`

実機向けにビルドするには、AndroidおよびvisionOSのBuild Supportモジュールを別途インストールする必要があります。
