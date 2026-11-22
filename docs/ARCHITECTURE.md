# Kiến trúc MYLTI Challenge Studio

## 1. Sơ đồ hệ thống

```text
 Concept Lab ──► RunPlanner ──► TenMinuteDirector ──────────────► CaptureCoordinator ──► VideoFileInspector
 (3 concept,      (cold open,     │  phases on ProgramClock          (Unity Recorder |       (MP4 mvhd/mehd/stts,
  score heur.)     5 rounds,       │  PacingEngine / StuckRecovery     OBS WebSocket |        MKV Info/Tracks)
                   final, summary) │                                   OBS manual)
                                   ▼
                        ChallengeSession ──► MegaRampBuilder (arena, ramp mesh, obstacles)
                          │  AttemptTracker ×N (takeoff, apex, landing, rollover, fall, stuck)
                          │  VehicleFactory → VehicleController (Rigidbody + 4 WheelCollider) ← AIDriver / keyboard
                          ▼
                     EventLogger (RunEvent, SegmentMarker) ── ReplaySystem (pose takes) ── HighlightFrameCapture (PNG)
                                   │
                                   ▼  after the take
   RunRecord ─► RoundScoring ─► HighlightScorer ─► FactSheetBuilder ─► OfflineContentGenerator ─► ClaimChecker
                                                                    (titles, description, A/B/C pairs)
                                   ─► QualityGate + NoveltyFingerprint ─► ContentPackService.Export
 CameraDirector (story shots, occlusion, min shot length)  ·  ShowOverlay (cards, scoreboard, replay badge)  ·  StudioUI (IMGUI)
```

## 2. Assemblies

| Assembly | Thư mục | Phụ thuộc | Ghi chú |
|---|---|---|---|
| `MYLTI.Domain` | `Scripts/Domain` | không (noEngineReferences) | Logic thuần C#: dữ liệu, planner, chấm điểm, fact sheet, content, claim check, quality, novelty, MP4/MKV, OBS WebSocket, preset JSON. Test được ngoài Unity. |
| `MYLTI.Runtime` | `Scripts/Runtime` | Domain, Unity.InputSystem (tuỳ chọn qua `versionDefines`) | Mọi thứ chạy trong scene. Không tham chiếu `UnityEditor`. |
| `MYLTI.Editor` | `Scripts/Editor` | Domain, Runtime, Unity.Recorder.Editor (tuỳ chọn) | Menu setup/build, `UnityRecorderBridge` chỉ biên dịch khi có `com.unity.recorder` ≥ 4.0. |
| `MYLTI.Tests.EditMode` | `Tests/EditMode` | Domain, NUnit | 19 test EditMode (Window ▸ General ▸ Test Runner). |

## 3. Ma trận yêu cầu P0 / P1

| Yêu cầu | Trạng thái | Nơi triển khai |
|---|---|---|
| FR-01 Mega Ramp arena | ✅ | `MegaRampBuilder`, `MeshBuilder` (dốc cong chuyển tiếp + mép nhảy, bãi đáp, mốc khoảng cách, vùng chết) |
| FR-02 3 xe | ✅ | `VehicleCatalog` (Sports Car, SUV, Heavy Truck), `VehicleFactory` |
| FR-03 Lái tay | ✅ | `InputReader` (Input System hoặc Input Manager), `VehicleController` |
| FR-04 AI | ✅ | `AIDriver` (pure pursuit theo làn, giữ tốc độ mục tiêu, full ga trên dốc, phanh sau tiếp đất) |
| FR-05 Vật lý/va chạm | ✅ | Rigidbody + WheelCollider, CCD, substeps, anti-roll, downforce |
| FR-06 Camera ≥ 4 | ✅ | `CameraDirector`: 8 góc + Auto + replay shots |
| FR-07 HUD/panel, ẩn HUD | ✅ | `StudioUI` (Creator/Capture mode, H, F1) |
| FR-08 Kết quả | ✅ | `AttemptTracker` → `AttemptRecord` |
| FR-09 Preset JSON | ✅ | `ScenarioConfig`, `PresetRepository` (clamp, xe không tồn tại, file hỏng → `.corrupt`) |
| FR-10 Editor + build | ✅ mã / ⏳ chưa build thử | `MyltiSetupMenu` |
| FR-11 Auto YouTube 10:00 | ✅ | `RunPlanner`, `TenMinuteDirector` |
| FR-12 Ghi dữ kiện hậu kỳ | ✅ | `EventLogger`, `RunRecord`, `events.json`, `highlights_edl.csv`, `shot_list.csv` |
| FR-13 Content Pack offline | ✅ | `TitleGenerator`, `DescriptionComposer`, `ThumbnailPromptBuilder`, `ContentPackService` |
| FR-14 Trạng thái video thật | ✅ | `CaptureCoordinator`, `VideoFileInspector.Classify` |
| §20 3 mức readiness | ✅ | `QualityGate` (SimulationComplete / MediaReady / ReadyForReview / PublishReady + NeedsMoreFootage) |
| §21 Concept Lab | ✅ (P0 lite) | `ConceptGenerator` (5 series, 3 concept, score heuristic), đối chiếu working title sau run |
| §22 Retention Director | ✅ (P0 lite) | hook thật, open loop (sub-question mỗi vòng), stakes, scoreboard, dead-air, payoff check |
| §23.1 Shot planner, frame thật | ✅ | `CameraDirector`, `HighlightFrameCapture` |
| §23.2 EDL/shot list | ✅ | `highlights_edl.csv`, `shot_list.csv` |
| §23.2 Quay phân đoạn + tự dựng | ⏳ P1 | — |
| §23.3 AudioDirector/nhạc/lời bình | ⏳ P1 (có âm thanh tổng hợp cơ bản) | `VehicleAudio` |
| §24 3 cặp Title+Thumbnail | ✅ | `OfflineContentGenerator` (A versus, B stakes, C search+twist) |
| §25 Analytics loop | ⏳ P1 | — |
| §26 Novelty / policy gate | ✅ | `NoveltyFingerprint`, `NoveltyHistory`, `ClaimChecker` |
| §5.1 Replay | ✅ lite (pose, 0.6×, góc khác) · ⏳ timeline scrub UI | `ReplaySystem` |
| §5.3 Bản đồ khác | ⏳ P1 (biến thể cầu/chướng ngại/mục tiêu có sẵn trên Mega Ramp) | — |
| §5.4 Hỏng hóc hình ảnh | ⏳ P1 | — |
| 9:16 | ⏳ P1 | — |

## 4. Hợp đồng dữ liệu

`ConceptBrief → RunPlan (PhaseSpec/RoundSpec) → RunRecord (RunEvent, AttemptRecord, SegmentMarker, CaptureInfo/VideoInfo) → RoundResult → Highlight → ContentFactSheet → ContentPack (TitleOption, TitleThumbnailPair) → QualityReport`

Tất cả có `WriteJson` (MiniJson, không phụ thuộc Unity). `null` = chưa xác minh.

## 5. Ba đồng hồ

| Đồng hồ | Nguồn | Dùng cho |
|---|---|---|
| Simulation | `Time.time` / `timeSinceLevelLoad` | vật lý |
| Program | `ProgramClock` (captureDeltaTime hoặc unscaled) | timeline 600 s, sự kiện, chapters |
| Media | thời lượng đọc từ tệp (`VideoFileInspector`) | `CaptureVerified` (598–602 s) |

## 6. Mặc định đã tự chọn (giả định)

- Unity 6.3 LTS + URP template; UI bằng IMGUI (không cần TextMeshPro/uGUI); camera tự viết (không cần Cinemachine).
- Fixed timestep 0.02 s; vehicle substeps (5, 12, 15); CCD ContinuousDynamic cho xe.
- Đường tăng tốc 260 m, làn rộng 9 m, bán kính chuyển dốc ≤ 120 m (giảm g-load ở tốc độ cao).
- Vòng xong sớm → tối đa 2 lượt bổ sung cho *mọi* xe (lượt tốt nhất được tính) — công bằng và có log `PacingDecision`.
- Điểm: thắng vòng 1 điểm, chung kết 2 điểm; hoà điểm → xe thắng chung kết; vẫn hoà → không có người thắng.
