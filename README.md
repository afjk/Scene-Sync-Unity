# Scene Sync Unity XRクライアント

主要なXRヘッドセットでSceneSyncコンテンツを受信するための、最小構成のUnityクライアントです。

## 基本構成

- Unity `6000.3.20f1`
- STYLY XR Rig `0.4.22`（commit `737d02a3741cdef9fe1f007bb3e0019911ce0b40` に固定）
- Scene Sync `0.26.3`
- Scene Sync Rapier（`afjk.jp` tag `v0.26.3`）
- Rapier for Unity `0.3.0`
- UnitySplats `1.2.0` / Unity.WebP `0.3.22`
- Universal Render Pipeline `17.3.0`
- PolySpatial visionOS `3.2.2`
- Unity CLI Loop `2.1.10`（開発・動作確認用）

初期シーンは `Assets/SceneSyncClient/Scenes/SceneSyncClient.unity` です。
再生するとSceneSyncへ自動接続します。`SceneSyncClientController` のRoomが空の場合は、
Presence Serverが送信元IPから割り当てるLAN内のRoomへ接続します。特定のRoomへ接続する場合だけ、
Roomへコードを設定してください。

シーンを再生成する場合は、Unity Editorで次のメニューを実行します。

`Tools > Scene Sync XR Client > Create Minimal Project Setup`

## Rapier物理同期

`SceneSyncRuntime`には`SceneSyncPhysicsMetadata`、`SceneSyncRapierBridge`、
`SceneSyncRapierInteractionController`を設定しています。Web側から受信したscene/objectの
physics metadataをRapier worldへ反映し、Shared Playbackの時刻に追従してsimulationします。

Rapier native libraryが同梱されているAndroid arm64、macOS arm64、Windows x86_64、
Linux x86_64でsimulationを有効化します。visionOSなどnative libraryがないplatformでは
physics metadataの同期を維持したままsimulationだけを無効化します。

## 3D Gaussian Splatting

`KHR_gaussian_splatting` GLBを受信すると、Scene Sync `0.26.3`が通常GLB経路から自動判定し、
UnitySplatsの実Gaussian rendererへ渡します。URP rendererには`Gsplat URP Feature`を設定済みです。
UnitySplatsが利用できない場合はScene Syncのpoint-previewへfallbackします。

UnitySplatsのXR対応はURP Single Pass InstancedまたはMulti-passです。Quest/PICO/VIVEおよび
visionOSでの性能・stereo表示・passthrough併用は、それぞれ実機で追加検証が必要です。

実機向けにビルドするには、AndroidおよびvisionOSのBuild Supportモジュールを別途インストールする必要があります。
