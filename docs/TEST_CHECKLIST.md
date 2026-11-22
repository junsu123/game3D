# Checklist kiểm thử (làm trên máy Windows 11 có Unity)

Ký hiệu: ☐ chưa làm · ✅ đã kiểm ngoài Unity (test tự động .NET) · 🎮 phải kiểm trong Unity

## A. Tự động (Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All)
✅ 19 test đã chạy đạt bằng .NET 8 ngoài Unity; ☐ chạy lại trong Unity Test Runner.

## B. Mốc A — vertical slice 🎮
- ☐ Mở dự án: Console **không lỗi compile** (cảnh báo “reference to non-existent assembly” khi chưa cài Recorder là bình thường).
- ☐ Play `MegaRampDemo`: thấy mặt đất, đường băng, dốc cam, mốc khoảng cách, 2 xe ở vạch xuất phát, ánh sáng.
- ☐ Tab Xe ▸ Sports Car = Manual ▸ Start: W/S/A/D/Space chạy được; R reset nhiều lần **không nhân bản xe**.
- ☐ Đổi camera 1–8, 0; F1 ẩn toàn bộ UI; H ẩn HUD.
- ☐ Xe lên dốc 8 m ở ~120 km/h: **không nảy bật ở đoạn chuyển cong**; nếu nảy → tăng `maxR` trong `MeshBuilder.KickerProfile` hoặc hệ số lò xo trong `VehicleFactory`.

## C. Mốc B 🎮
- ☐ AI (3 xe) chạy thẳng làn, đạt ~tốc độ mục tiêu trước dốc (xem HUD); nếu lắc → giảm hệ số `1.6f` trong `AIDriver.SteerToward`.
- ☐ Kết quả lượt: Outcome, khoảng cách, tốc độ, độ cao, thời gian bay.
- ☐ Save preset → đóng app → mở lại → Load → cấu hình giống.
- ☐ Sửa tay tệp preset thành JSON hỏng → Load báo lỗi, tệp đổi thành `.corrupt`.
- ☐ Preset có `vehicleId` không tồn tại → bị loại kèm cảnh báo.

## D. Mốc C — 10:00 🎮
- ☐ US-01: Concept Lab ▸ 2 xe ▸ Sinh 3 concept ▸ Generate Plan → timeline 00:00–10:00, 5 vòng + chung kết, biểu đồ độ cao tăng dần.
- ☐ US-02: Start & Capture (Không ghi) → chạy hết, số vòng không nhảy khi xe được hồi sinh; không dừng vì kẹt.
- ☐ Cold open: thấy cú nhảy thật trong 5 giây đầu, **bị cắt trước khi tiếp đất**.
- ☐ US-03 (Unity Recorder): tệp `Runs/<id>/video/*.mp4` xuất hiện, tab Video Capture báo `CaptureVerified` 598–602 s. Xoá tệp rồi Verify → `VideoMissing`.
- ☐ US-03 (OBS WebSocket, bản build): OBS tự bắt đầu/dừng; đường dẫn được nhận; thời lượng được đọc.
- ☐ US-04/US-07: chạy một run mà không xe nào lật → tiêu đề/mô tả không có “crash/rollover”; run hoà → không có “Wins”.
- ☐ US-05: YouTube Pack có 5 tiêu đề, 1 mô tả, 3 prompt; Copy dán được; Export All tạo đủ tệp, tiếng Việt không lỗi font.
- ☐ US-06: Rút mạng → vẫn sinh Content Pack.
- ☐ US-08: Esc giữa run → trạng thái Interrupted; Abort → `Incomplete`; recorder dừng giữa chừng → không báo thành công.
- ☐ US-09: chạy 2 lần → 2 thư mục `runId` khác nhau.
- ☐ US-10: mở `frames/*.png` — prompt thumbnail mô tả đúng xe/màu/dốc trong frame.

## E. Bài nghiệm thu §29 🎮
- ☐ Quality fail-safe: cho xe kẹt lâu (đặt tốc độ mục tiêu thấp nhất + dốc 30 m) → `NeedsMoreFootage`, không `PublishReady`.
- ☐ Originality: chạy lại cùng concept chỉ đổi màu xe → cảnh báo trùng lặp (Quality ▸ novelty); đổi series → điểm khác biệt tăng.
- ☐ PublishReady chỉ xuất hiện sau khi tick “Tôi đã xem toàn bộ video…”.

## F. Hiệu năng 🎮
- ☐ Profiler: 1–3 xe, Quality High, 1080p — ghi lại fps thực tế (60 fps là mục tiêu, không cam kết).
- ☐ Xe ở tốc độ cao không xuyên dốc/mặt đất (CCD); xe rơi khỏi bản đồ → `FellOff`.
