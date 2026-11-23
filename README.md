# MYLTI Challenge Studio — Unity 6 (C#) · Windows 11

Công cụ mô phỏng thử thách xe 3D để **sản xuất video YouTube 10:00**: chọn concept → lập kế hoạch 5 vòng + chung kết → chạy & quay một take liền mạch → xác minh tệp video → sinh **YouTube Content Pack** (5 tiêu đề, mô tả SEO, 3 cặp title/thumbnail prompt) chỉ từ dữ kiện thật → Quality Gate.

> **Trạng thái kiểm thử (trung thực):**
> - ✅ Toàn bộ 40 tệp mã C# trong `Scripts/` **đã biên dịch không lỗi** với bộ stub API Unity 6.3 (chữ ký đối chiếu mã nguồn tham chiếu Unity 6000.3), cả hai chế độ Input System mới và Input Manager cũ, kèm Unity Recorder.
> - ✅ Phần logic thuần (`Scripts/Domain`) biên dịch cho .NET Standard 2.1 và **19/19 test** chạy đạt bằng .NET 8: planner 600 s, chấm điểm, Content Pack, chống bịa, Quality Gate, novelty, đọc thời lượng MP4/MKV, preset hỏng.
> - ❌ **Chưa chạy trong Unity Editor, chưa build .exe, chưa quay video thật** (môi trường làm việc là Linux, không có Unity). Các hằng số vật lý (lò xo, dốc, AI) là giá trị khởi điểm hợp lý — cần bấm Play và tinh chỉnh theo checklist ở `docs/TEST_CHECKLIST.md`.

---

## 1. Cài đặt trên Windows 11 (3 bước)

1. Unity Hub ▸ **New project** ▸ template **Universal 3D** ▸ Create.
2. Trên trang GitHub này bấm **Code ▸ Download ZIP**, giải nén, rồi chép thư mục `Assets/_MYLTI` vào thư mục `Assets` của dự án (trong Unity: cửa sổ *Project* ▸ chuột phải `Assets` ▸ **Show in Explorer**).
3. Chờ Unity biên dịch xong rồi bấm **▶ Play**. Studio tự khởi động trong bất kỳ scene nào (kể cả `SampleScene`) — không cần tạo scene.

**Ra tệp .exe (tuỳ chọn):** menu **MYLTI ▸ Build Windows x64** → thư mục chứa `MYLTI Challenge Studio.exe` tự mở ra; chép cả thư mục đó sang máy nào cũng chạy bằng cách nhấp đúp.

*(Tuỳ chọn – quay trong Editor)* **Window ▸ Package Manager ▸ + ▸ Add package by name…** → `com.unity.recorder`.

Không cần mạng, tài khoản hay API trả phí. Dữ liệu người dùng nằm tại
`%USERPROFILE%\AppData\LocalLow\MYLTI\MYLTI Challenge Studio\` (menu **MYLTI ▸ Open Data Folder**):
`Presets/`, `Runs/<runId>/`, `settings.json`, `episode_history.json`.

## 2. Dùng thử nhanh

| Bước | Làm gì |
|---|---|
| Sandbox | Tab **Thử thách** chỉnh dốc/góc/chướng ngại → **Start**. Tab **Xe** bật/tắt 3 xe, màu, AI/Manual. |
| Lái tay | Chế độ lái = Manual → **W/S** (↑/↓) ga-lùi, **A/D** lái, **Space** phanh, **R** reset. |
| Camera | **1** Chase · **2** Side · **3** Front/Lip · **4** Impact · **5** Wide · **6** Top · **7** Orbit · **8** Free (chuột phải + WASD, Q/E) · **0** Auto |
| Concept Lab | Chọn series → **Sinh 3 concept** → chọn 1 (sửa câu hỏi/working title nếu muốn) → **Generate Run Plan**. |
| 10-Min Director | Xem/sửa kế hoạch 10:00 (độ cao, góc, tốc độ, chướng ngại, luật từng vòng) → **Start & Capture**. |
| Video Capture | Chọn **Unity Recorder** (Editor), **OBS WebSocket** (khuyên dùng cho bản build), **OBS thủ công** hoặc **Không ghi**. |
| YouTube Pack | Sửa/copy 5 tiêu đề, mô tả, 3 prompt → **Export All**. |
| Quality | Xem readiness; tick *“Tôi đã xem toàn bộ video…”* để lên `PublishReady`. |
| Phím chung | **F1** Creator ↔ Capture Mode (sạch, không UI) · **H** ẩn HUD · **Esc** tạm dừng run (đánh dấu *Interrupted*). |

### Quay bằng OBS (bản build Windows)
1. OBS 28+ ▸ **Tools ▸ WebSocket Server Settings** ▸ Enable, đặt mật khẩu. Output ▸ Recording: định dạng **MP4** (hoặc MKV), 1920×1080.
2. Thêm nguồn *Window Capture* cửa sổ MYLTI.
3. Trong app: tab **Video Capture** → *OBS qua WebSocket*, nhập host/port/mật khẩu → **Kiểm tra kết nối OBS**; điền *Thư mục ghi của OBS*.
4. **Start & Capture**: app tự bấm StartRecord ở 00:00 và StopRecord ở 10:00, nhận đường dẫn tệp và **đọc thời lượng thật** (598–602 s mới là `CaptureVerified`).
   *OBS thủ công*: bạn tự bấm ghi/dừng → tệp sẽ dài hơn 600 s do thao tác tay → `InvalidDuration` cho tới khi bạn cắt đầu/cuối và **Verify** lại; chapters bị bỏ vì không biết độ lệch.

## 3. Một run 10:00 gồm gì

`Cold Open (cú nhảy thật, cắt trước khi tiếp đất) → Giới thiệu xe & luật → Vòng 1 Baseline → Vòng 2 cao/nhanh hơn → Vòng 3 Twist → Vòng 4 Leo thang → Vòng 5 Cực hạn → Chung kết (tất cả xe cùng lúc, điểm ×2) → Kết quả + replay khoảnh khắc thật → 10:00`

- Mỗi vòng đổi ít nhất một biến có ý nghĩa (độ cao, tốc độ, chướng ngại, luật, cầu hẹp, dốc tiếp đất) — ghi trong `changeSummary`.
- **PacingEngine:** vòng xong sớm → thêm lượt cho *mọi* xe (lượt tốt nhất được tính) hoặc replay pose thật; trễ → bỏ phần tuỳ chọn; phần kết khép đúng 600 s trên đồng hồ chương trình. Không chèn cảnh đứng yên.
- **StuckDetector:** đứng yên > 8 s → hồi sinh (tối đa 2 lần/lượt, có log) → quá thì `Stuck`.
- Đồng hồ chương trình = thời gian video: với Unity Recorder chạy theo `Time.captureDeltaTime` (frame cố định), với OBS theo thời gian thực.

## 4. Tệp xuất ra (`Runs/<runId>/`)

`youtube_titles.txt` · `youtube_description.txt` · `thumbnail_prompts.txt` · `content_pack.json` · `youtube_pack.json` · `titles_and_thumbnail_variants.csv` · `fact_sheet.json` · `round_results.json` · `events.json` · `run_record.json` · `quality_report.json` · `highlights_edl.csv` (start/end/eventId/score/camera/reason/sourceClip) · `shot_list.csv` · `asset_manifest.json` · `frames/frame_evXXXX.png` (frame thật tại điểm cao nhất/lật xe) · `video/…mp4` (khi dùng Unity Recorder).

`runId` luôn duy nhất — không ghi đè run cũ.

## 5. Nguyên tắc “không bịa” đã cài sẵn

- `MetadataGenerator` chỉ đọc `ContentFactSheet` (dựng từ telemetry). Số liệu thiếu = `null` → câu chữ bớt cụ thể, không đoán.
- `ClaimChecker` từ chối: số không có trong fact sheet; “crash/rollover/fell off” khi không có sự kiện đó; “wins/winner” khi không có người thắng; nhãn hiệu thật (Ferrari, BeamNG…); “realistic damage / real-world physics / scientific proof”. Câu hỏi (“Who Wins?”) được phép.
- Không có `CaptureVerified` nếu chưa đọc được tệp và thời lượng. `PublishReady` luôn cần chủ kênh duyệt.
- Bộ tạo nội dung là **template offline có điều kiện — không phải AI**. Interface `IContentGenerator` để gắn LLM sau (tuỳ chọn, kết quả vẫn bị ClaimChecker kiểm tra lại và rơi về offline nếu sai/lỗi mạng).

## 6. Giới hạn đã biết

- **Chưa chạy thử trong Unity**: cần tinh chỉnh vật lý (bán kính chuyển dốc, lò xo, ổn định trên không, AI bám làn) khi bấm Play lần đầu.
- Unity Recorder chỉ có trong Editor; bản build dùng OBS. Không có trình giải mã video: app kiểm tra container/thời lượng/track âm thanh, **không** xác nhận phát được — hãy mở tệp xem thử.
- Một bản đồ Mega Ramp (có biến thể cầu hẹp, thùng phuy, tường thùng, vòng mục tiêu, dốc tiếp đất). Crash Wall / Drop Test / Obstacle Run riêng là P1.
- Thân xe cứng, không biến dạng; âm thanh động cơ/va chạm là tổng hợp thủ tục (bản quyền tự có). Chưa có nhạc nền/lời bình.
- Chỉ 16:9 1920×1080. 9:16, quay phân đoạn + tự dựng, timeline scrub cho replay, Analytics import, Underdog Comeback series: P1.
- Mật khẩu OBS chỉ lưu khi bạn bật “Nhớ mật khẩu” (dạng văn bản trong `settings.json` trên máy bạn).
- Giao diện IMGUI dùng font mặc định của Unity; dấu tiếng Việt dựa vào font dự phòng của Windows.

Kiến trúc, ma trận P0/P1 và hợp đồng dữ liệu: `docs/ARCHITECTURE.md`. Checklist kiểm thử: `docs/TEST_CHECKLIST.md`.
