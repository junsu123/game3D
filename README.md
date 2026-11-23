# MYLTI Challenge Studio — Unity 6 (C#) · Windows 11

Công cụ mô phỏng thử thách xe 3D để **sản xuất video YouTube 10:00**: chọn concept → lập kế hoạch 5 vòng + chung kết → chạy & quay một take liền mạch → xác minh tệp video → sinh **YouTube Content Pack** (5 tiêu đề, mô tả SEO, 3 cặp title/thumbnail prompt) chỉ từ dữ kiện thật → Quality Gate.

> **Trạng thái kiểm thử (trung thực):**
> - ✅ Toàn bộ 80 tệp mã C# trong `Scripts/` **đã biên dịch không lỗi** với bộ stub API Unity 6.3 (chữ ký đối chiếu mã nguồn tham chiếu Unity 6000.3), cả hai chế độ Input System mới và Input Manager cũ, kèm Unity Recorder, nhánh nạp mẫu xe .glb (glTFast) và khán giả có khung xương (SkinnedMeshRenderer, Playables).
> - ✅ Phần logic thuần (`Scripts/Domain`) biên dịch cho .NET Standard 2.1 và **73/73 test** (thêm xe hoạt hình: lưới hợp lệ, mặt ở mũi xe, mí mắt/con ngươi, biểu cảm, chọn dàn xe) chạy đạt bằng .NET 8 (thêm: khán giả — lưới người hợp lệ, đội siêu anh hùng tự sáng tạo, tư thế vỗ tay/giơ tay, cảm xúc đám đông theo sự kiện, tiếng reo hò/vỗ tay; vòng đâm tường/thả rơi chấm theo hư hại, nạp mẫu xe thật + công tắc tên hãng, đọc HDRI, trộn nhạc/tiếng va chạm, lời bình, chống spoiler, parse kịch bản Claude, xoay vòng không trùng, trộn âm thanh, thẻ chữ/hook, chuẩn hoá chữ cho Kokoro, giờ mùa hè Mỹ/EU, chọn giờ đăng, hàng đợi, JSON tải lên YouTube, PKCE, kiểm tra bản dịch): planner 600 s, chấm điểm, Content Pack, chống bịa, Quality Gate, novelty, đọc thời lượng MP4/MKV, preset hỏng.
> - ❌ **Chưa chạy trong Unity Editor, chưa build .exe, chưa quay video thật, chưa gọi thử Claude API / TTS thật, chưa chạy Kokoro trên Windows, chưa tải video thật lên YouTube, chưa nạp thử mẫu xe .glb thật, chưa xem móp méo/HDRI/khán giả chuyển động trên màn hình** (môi trường làm việc là Linux, không có Unity). Riêng bộ lọc chữ FFmpeg đã được chạy thật với FFmpeg 6.1 trên video thử và kiểm tra bằng mắt; hình khán giả/siêu anh hùng đã được dựng thử bằng trình vẽ phần mềm để kiểm tra dáng và tư thế. Các hằng số vật lý (lò xo, dốc, AI) là giá trị khởi điểm hợp lý — cần bấm Play và tinh chỉnh theo checklist ở `docs/TEST_CHECKLIST.md`.

---

## ▶ Auto Episode — một nút ra video hoàn chỉnh

Tab đầu tiên **▶ Auto Episode** → bấm **AUTO EPISODE**:

1. **FFmpeg**: tự tải một lần (~150 MB, từ github.com/BtbN/FFmpeg-Builds) vào thư mục dữ liệu của app.
2. **Kịch bản**: Claude viết concept + 6 vòng (5 vòng tăng dần + chung kết). Lịch sử các tập trước được gửi kèm để **không trùng**; nếu quá giống (> 85 %) app yêu cầu viết lại. Mọi giá trị được kiểm tra/giới hạn. Không có key → bộ viết offline xoay vòng series × twist × đội hình chưa dùng.
3. **Tự quay**: camera render 1920×1080 vào RenderTexture → FFmpeg (H.264). Khoá `Time.captureFramerate` = 60 (mặc định, chọn được 30) nên video đúng từng khung (10:00 = 36 000 khung) kể cả máy yếu. Không có UI/bảng điều khiển trong video (F1 không bao giờ vào video); chỉ có **tên xe + tốc độ km/h trên đầu xe** (vật thể 3D trong cảnh) — tắt được.
4. **Lời bình tiếng Anh**: sau khi quay, Claude viết từng câu cho từng khoảnh khắc dựa trên sự kiện THẬT (cất cánh, tiếp đất, lật, kết quả vòng). Mỗi câu bị kiểm tra: không tiết lộ trước kết quả, không số liệu bịa, không nhãn hiệu, đúng độ dài khung thời gian; câu sai → thay bằng mẫu offline.
5. **Giọng đọc** (xem bảng bên dưới): mặc định **Kokoro** — miễn phí, chạy trên máy, dùng thương mại được. Lỗi/hết hạn mức → tự chuyển giọng Windows.
6. **Âm thanh**: dựng offline trên đúng timeline: lời bình + tiếng động cơ (theo tốc độ thật), gió khi bay, va chạm, nền nhẹ; tự giảm nhạc nền khi có lời.
7. **MP4 cuối** `Runs/<runId>/FINAL_<tiêu-đề>.mp4` (H.264 + AAC) — được kiểm tra thời lượng thật. **Thẻ chữ in vào video** (FFmpeg drawtext, mờ dần vào/ra):
   - 0–4 s: tiêu đề hook lớn (vd. *22 METER MEGA RAMP — WATCH THIS*) đè lên cảnh teaser.
   - Thẻ tiêu đề: câu hỏi của tập + **"COMING UP: …"** liệt kê những thứ *thật sự* sẽ xuất hiện (dốc 22 m, tường thùng, cú lật…).
   - Trước mỗi thử thách: **ROUND n / THE FINAL** + mô tả thử thách (độ cao dốc, cầu hẹp, chướng ngại, luật) + **một câu gây tò mò** về cảnh sau (vd. *NOT EVERYONE LANDS ON THEIR WHEELS* chỉ khi vòng đó thật sự có xe lật; *A NEW LONGEST JUMP IS COMING* chỉ khi kỷ lục thật sự bị phá). Không bao giờ lộ người thắng trước.
   - Kết quả vòng, nhãn REPLAY, bảng xếp hạng cuối, lời kêu gọi bình luận.
   - Có Claude key → Claude viết lại câu hook cho cuốn hơn; mỗi câu bị kiểm tra (không số bịa, không “crash/flip” nếu không có thật, không tên hãng, không nêu người thắng), sai → giữ câu mẫu. Danh sách thẻ lưu ở `video_cards.json`.
8. **Kèm theo**: 5 tiêu đề, mô tả SEO có chapters, `captions_en.srt` (phụ đề tải lên YouTube, không in vào hình), `thumbnail_1280x720.jpg` (từ khung hình thật), `narration_script.json`.

### Giọng đọc — nên chọn gì?

| Lựa chọn | Chất lượng | Giá | Dùng cho kênh kiếm tiền |
|---|---|---|---|
| **★ Kokoro-82M** (mặc định) — model mã nguồn mở, chạy qua [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) | Tự nhiên, gần giọng thương mại | **Miễn phí, không giới hạn**, offline sau lần tải đầu (~150 MB) | ✅ Apache-2.0 |
| ElevenLabs | Hay nhất, nhiều cảm xúc (model `eleven_multilingual_v2`) | Free 10k credit/tháng (~10k ký tự; 1 tập 10 phút ≈ 5–8k ký tự lời bình) | ❌ gói Free không cho thương mại → cần gói trả phí |
| Google Cloud TTS | Khá | Hạn mức miễn phí hằng tháng, cần key | ✅ |
| Giọng Windows | Cơ bản (robot) | Miễn phí | ✅ |

Kokoro có 14 giọng tiếng Anh để chọn trong app (af_heart chất lượng cao nhất; am_michael / am_fenrir giọng nam hợp bình luận), nút **▶ Nghe thử giọng**. ElevenLabs: chọn model, bấm **Liệt kê giọng trong tài khoản** để chọn voice. Số trong câu được đọc thành chữ (“18.5 m” → “eighteen point five metres”) để Kokoro phát âm đúng.

**Chi phí & điều kiện:** Claude API tính phí theo token (tách biệt gói Claude.ai) — lấy key tại console.anthropic.com. Giọng Windows và bộ viết offline thì miễn phí hoàn toàn. Trước khi đăng hãy xem lại video: app không thể tự đảm bảo nội dung hợp chính sách YouTube.

Lần đầu nên bấm **Quay thử 3 giây** để kiểm tra hình không bị lộn ngược (nếu ngược, đổi ô *Lật dọc*).

## 📅 Lịch & tự đăng YouTube (Mỹ + châu Âu)

Tab **📅 Lịch & Đăng**:
1. **Kết nối kênh** bằng *Đăng nhập Google* (OAuth). App **không cần và không nhận mật khẩu** của bạn; quyền chỉ là tải video và phụ đề, có thể thu hồi bất cứ lúc nào. Bạn cần tự tạo một OAuth Client loại *Desktop app* trong Google Cloud (hướng dẫn từng bước có trong app, khoảng 10 phút, làm một lần).
2. **Lịch đăng** chọn giờ theo múi giờ khán giả và tự tính giờ mùa hè. Mặc định 3 video/tuần. Ngày thường ≈ 12:30 New York / 18:30 Trung Âu; cuối tuần ≈ 10:30 New York (giờ mùa đông). Tỉ trọng 17 thị trường chỉnh được; nghiên cứu và nguồn ở `docs/MARKET_RESEARCH.md`.
3. **Châu Âu**: Claude dịch tiêu đề và mô tả sang tiếng Đức, Pháp, Tây Ban Nha, Ý, Hà Lan, Ba Lan, Thuỵ Điển, Đan Mạch, Phần Lan. Bản dịch nào đổi số liệu hoặc mốc chương thì bị bỏ. Bạn nên bật thêm *Automatic dubbing* trong YouTube Studio.
4. **Autopilot**:
   - Luôn làm sẵn N tập. Mỗi tập được tải lên ở chế độ **Riêng tư kèm giờ công khai hẹn sẵn**, cùng thumbnail và phụ đề tiếng Anh. YouTube tự công khai đúng giờ.
   - Chỉ đăng tập có video `CaptureVerified` và qua Quality Gate. Tập lỗi bị bỏ qua và ghi lý do. Tải lên thất bại thì tự thử lại; hết hạn mức thì đợi hôm sau.
   - Bản .exe có thể tạo lịch **Windows Task Scheduler**: mỗi ngày app tự mở (có thể đánh thức máy), làm và tải phần còn thiếu, rồi tự đóng.
   - Máy phải bật và đã đăng nhập Windows lúc chạy.
5. Lưu ý chính sách: YouTube không cho kiếm tiền với nội dung *sản xuất hàng loạt hoặc lặp lại*. App có cơ chế chống trùng nhưng không thể đảm bảo kênh được duyệt kiếm tiền. Không nên đăng quá dày; nên xem lại các tập đầu.

## 🚗 Xe thật, móp méo, thử thách va chạm

**Xe giống ngoài đời thực** — dùng mẫu 3D do bạn tải về (app không tự có mẫu xe hãng):
1. Lần đầu mở dự án, Unity hỏi cài **glTFast** → *Cài ngay* (hoặc menu **MYLTI ▸ Install glTFast**).
2. Tải mẫu xe dạng **.glb/.gltf** (Sketchfab, CGTrader, Unity Asset Store…) → tab **Xe** ▸ *Mở thư mục Vehicles* → thả file vào → *Nạp lại xe*. (File .fbx: đặt vào `Assets/_MYLTI/Resources/Vehicles`.)
3. App tự: xoay mũi xe về phía trước, chỉnh kích thước thật, đặt xuống đất, **tìm 4 bánh** (theo tên `wheel/tire/rim…`, không có thì theo hình dạng), gắn bánh quay + giảm xóc + va chạm, tách kính để vỡ. Kết quả hiện ngay dưới tên xe (vd. “wheels found by name” / “model faces backwards → yawDeg 180”).
4. Mỗi mẫu có file `<tên>.vehicle.json` (app tạo sẵn, sửa được trong tab Xe): hãng, mẫu, **tên chung**, loại (hatchback/sedan/sports/supercar/suv/pickup/van/truck/bus), khối lượng, công suất, tốc độ tối đa, chiều dài thật, xoay, **giấy phép/tác giả**. Vật lý (mô-men, phanh, lò xo, chống lật) tính từ các số này. Có ≥ 2 xe thật → Auto Episode chỉ dùng xe thật.
5. **Tên hãng**: công tắc *Dùng tên hãng thật* (mặc định bật). Bật: tiêu đề/lời bình/chữ trên video gọi đúng “Toyota Hilux”; mô tả video tự ghi “mô phỏng game, không phải thử nghiệm va chạm thật; kênh không liên kết với {hãng}” + dòng ghi công tác giả mẫu 3D. Tên hãng KHÁC (không có trong xe của bạn) vẫn bị chặn để không bịa. Tắt: dùng tên chung (“Pickup Truck”).
   ⚠️ Tên/logo/kiểu dáng xe là nhãn hiệu của hãng: có rủi ro khiếu nại; tránh mẫu ghi *Editorial use only* nếu kênh kiếm tiền; mẫu CC-BY phải ghi tên tác giả (dán vào ô Giấy phép).

**Móp méo khi va chạm** (mọi xe, kể cả 3 xe mẫu): thân xe lõm theo điểm va chạm (biến dạng lưới đỉnh, nếp nhăn), **vỡ kính** (mảnh kính bay), **văng bánh** khi đâm mạnh cạnh bánh, tia lửa + khói. **% hư hại** là ước tính của game (tăng dần, bão hoà về 100 %; xe nặng khung chắc hơn) — dùng để chấm điểm, không phải độ an toàn thật. Replay chiếu lại đúng trình tự: xe còn nguyên trước cú va, móp đúng lúc va. Thanh *Độ móp khi va chạm* 0–2.5×.

**Thử thách mới** (series **Crash Lab** + twist *crash / crash_drop / crash_wall* cho các series khác; Claude cũng được dùng):
- **Đâm tường bê tông** ở tốc độ đặt trước (30–160 km/h): xe ít hư hại nhất thắng; ghi tốc độ lúc va thật.
- **Thả rơi từ cần cẩu** (5–60 m), xe chúi mũi: xe ít hư hại nhất thắng (lật vẫn tính, hư hại thường nặng hơn).
- **Nhảy ít hư hại nhất** (Mega Ramp, chấm theo hư hại thay vì khoảng cách).
- Ví dụ Crash Lab: nhảy 6 m → đâm 50 km/h → thả 15 m → đâm 90 km/h → thả 35 m → chung kết nhảy cùng lúc, ít hư hại nhất thắng.

**Quay như phim**: góc máy cạnh tường nhìn xe lao tới, góc thấp dưới cần cẩu, **replay chiếu chậm 0.2× đúng lúc va chạm** (chậm dần vào, nhanh dần ra), rung máy theo lực va, teaser mở đầu cắt ngay trước cú va (giữ tò mò), thumbnail lấy khung hình lúc va.

**Nhạc nền & bầu trời thật**:
- Thả nhạc (.mp3/.wav/.m4a/.ogg) vào thư mục **Music** (nút trong tab ▶ Auto Episode) — mỗi tập một bài, tự nhỏ đi khi có lời bình, fade đầu/cuối. Chỉ dùng nhạc bạn có quyền dùng trên YouTube (gợi ý: YouTube Studio ▸ Audio Library).
- **Bầu trời HDRI chụp thật** (Poly Haven, CC0, tự tải vài MB một lần): skybox + ánh sáng môi trường + phản chiếu trên thân xe/kính. Có thể thả file .hdr của bạn vào thư mục **Sky**. Không tải được → dùng bầu trời mặc định.
- Âm thanh va chạm kim loại + vỡ kính được tổng hợp theo đúng thời điểm và lực va.

## 🚗😀 MYLTI Toons — xe hoạt hình của kênh

![MYLTI Toons](docs/images/mylti_toons.png)
![Biểu cảm](docs/images/mylti_toons_faces.png)

- 6 xe hoạt hình **do kênh tự thiết kế** — không logo, không tên hãng, không dựa trên nhân vật phim nào: **Zippy** (xe nhỏ xanh lá, lanh chanh), **Bruno** (bán tải địa hình cam, đèn nóc + khung chống lật), **Duchess** (coupe tím sang chảnh), **Turbo Tito** (xe đua xanh ngọc có cánh gió), **Big Bao** (xe van tròn xanh trời), **Rumble** (xe tải quái vật tím bánh khổng lồ).
- **Mặt sống động**: mắt là đèn pha có mí, con ngươi, lông mày; miệng trên cản trước. Biểu cảm tự đổi theo chuyển động thật: chớp mắt, nhìn camera khi đứng yên, thỉnh thoảng nháy mắt; nhíu mày quyết tâm khi tăng tốc; trợn mắt + miệng chữ “O” khi bay (nhìn xuống khi rơi); nhắm tịt mắt khi đâm; cười toe khi đáp; hoảng loạn, mắt đảo vòng khi lật ngửa. Chạy cả trong replay.
- **Thân xe nhún kiểu hoạt hình** (bẹp xuống khi tiếp đất, dãn ra khi bật lên) — chỉ là hình ảnh, vật lý không đổi. Xe vẫn móp/vỡ kính khi va chạm.
- Mỗi xe có vật lý riêng (khối lượng, lực kéo, giảm xóc): Rumble nặng và nảy, Turbo Tito nhanh nhất, Zippy nhẹ bay xa.
- Tab ▶ Auto Episode ▸ **Dàn xe**: *Xe hoạt hình MYLTI Toons* (mặc định) · *Xe thật / xe mẫu* · *Trộn cả hai*.

## 🦸 Khán giả hoạt hình + đội siêu anh hùng MYLTI Squad

![MYLTI Squad](docs/images/mylti_squad.png)
![MYLTI Legends](docs/images/mylti_legends.png)

- **Khán đài 5 bậc có mái** dọc đường băng, **đám đông đứng sau hàng lốp** ở vùng xe đáp, **hàng rào khán giả** cạnh tường va chạm / cần cẩu thả rơi. Khoảng 1.100 người (chỉnh được), mỗi người là một nhân vật 3D có khung xương: tay, chân, đầu cử động thật.
- **Phản ứng theo đúng sự kiện thật** của lượt chạy: xe vào dốc → xôn xao; cất cánh → giơ tay reo hò, nhảy lên, đấm tay; đáp/đâm → “ồ!”; có kết quả vòng → **vỗ tay rào rào**; bảng xếp hạng cuối → vỗ tay lâu. Mọi người **quay đầu nhìn theo xe**, người đứng xa phản ứng chậm hơn một chút.
- **MYLTI Squad** — 7 siêu anh hùng **do kênh tự sáng tạo** (không dựa trên nhân vật có bản quyền nào): Turbo Ranger, Nova Spark, Gear Guardian, Comet Kid, Rocket Rex, Pixel Knight, Aurora Dash. Bộ đồ liền thân, mặt nạ, găng, ủng, thắt lưng, áo choàng, biểu tượng ngực riêng; có cả **người lớn và các bạn nhỏ**. Tỉ lệ khán giả mặc đồ siêu anh hùng chỉnh trong tab ▶ Auto Episode (mặc định 60 %).
- **Tiếng khán giả trong video**: tiếng rì rầm, reo hò (“aaah”), vỗ tay, “ồ” khi va chạm — tổng hợp từ đúng đường cong cảm xúc đã điều khiển khán giả trên hình, nên **tiếng khớp với hình**. Tự nhỏ đi khi có lời bình.
- **Muốn người giống thật hơn**: tải nhân vật + động tác (Idle, Clapping, Cheering…) miễn phí từ Mixamo dạng FBX, bỏ vào `Assets/_MYLTI/Resources/Crowd` (xem `README_Crowd.txt` trong đó) → hàng đầu tự dùng, phía sau vẫn là khán giả tự tạo cho nhẹ máy.
- ⚠️ Không dùng mô hình/fan-art của siêu anh hùng có bản quyền (kể cả “đổi màu”) — vẫn bị nhận ra → dễ dính Content ID / khiếu nại.
- **MYLTI Legends** — 6 chiến binh **phong cách game bắn súng/hành động**, cũng do kênh tự sáng tạo: Ember Ronin (mũ trùm + khăn che mặt + giáp vai + khăn chéo), Volt Striker (tóc mohawk + kính bảo hộ + găng giáp, tay trần), Desert Drifter (mũ rộng vành + khăn che mặt + áo khoác dài), Steel Hornet (mũ bảo hiểm kín + kính chắn + giáp vai gai), Jade Warden (mũ trùm + áo khoác dài + giáp vai), Night Courier (kính bảo hộ + mặt nạ + áo khoác dài). Có cả phiên bản trẻ em.
- Thanh *Tỉ lệ khán giả hoá trang* chia đều cho cả hai đội. Đây là khán giả ít đa giác nhìn từ xa — không đạt độ chi tiết như ảnh quảng cáo game; muốn cận cảnh đẹp như vậy cần mô hình 3D chất lượng cao có giấy phép thương mại (bỏ vào Resources/Crowd).
- Nếu kênh nhắm tới trẻ em: bật **“Video dành cho trẻ em (Made for Kids)”** ở tab 📅 Lịch & Đăng (bạn tự khai báo theo luật COPPA).

## 🎬 Quay dựng kiểu truyền hình đua xe

- **Ống kính tele bên đường** (như camera F1/MotoGP): máy đặt xa đường băng, tự zoom sao cho **xe chiếm ~45 % bề ngang khung hình** dù xe ở gần hay xa; tele còn “nén” khán đài phía sau nên **khán giả trông to và đông**. Mỗi góc máy tự tính độ zoom theo kích thước xe (xe tải/bus tự lùi khung).
- **Máy có người vận hành**: vị trí, điểm nhìn và zoom đều có quán tính (giảm chấn), máy **nhìn hơi trước mũi xe** (chừa khoảng trống phía trước), không bao giờ giật theo từng khung. Rung tay máy rất nhẹ và **rung va chạm dùng nhiễu Perlin mượt**, tự giảm khi ống kính tele (chỉnh được, 0 = chân máy cố định).
- **Quy tắc dựng**: máy bên đường luôn ở cùng một phía (quy tắc 180°); hai góc giống nhau thì **lướt máy mượt 1–1.4 s** thay vì cắt (tránh “jump cut”, quy tắc 30°); vào khoảnh khắc lớn (lên dốc, tiếp đất, đâm tường) thì **cắt theo hành động**; mỗi góc giữ tối thiểu 2.2 s; vào góc quay vòng thì bắt đầu đúng từ vị trí máy đang đứng nên không bị khựng.
- **Cắt cảnh khán giả**: sau cú đáp/va chạm ấn tượng và khi công bố kết quả vòng, máy cắt sang **hàng khán giả đầu đang reo hò/vỗ tay** (cận vài người, đẩy máy chậm vào) rồi quay lại xe — giống truyền hình thể thao.
- **Tên xe + tốc độ to hơn ~50 %** và giữ cùng cỡ trên màn hình với mọi ống kính; thanh *Cỡ chữ tên xe / tốc độ* trong tab ▶ Auto Episode.
- **Mặc định quay 60 khung/giây** (mượt hơn trên YouTube; vì quay theo bước cố định nên máy yếu chỉ mất thời gian quay lâu hơn, video không bị giật). Có thể chọn lại 30 ở tab Video Capture.
- Phím: **9** = góc khán giả.

## Đồ hoạ

- **Xe**: thân xe bo tròn dựng thủ tục (loft các mặt cắt superellipse) — xe thể thao có kính bong bóng, sọc đua, cánh gió; SUV có giá nóc; xe tải có cabin + thùng hàng; mâm 5 chấu, đèn phát sáng. Va chạm vẫn dùng hộp đơn giản (ổn định).
- **Sân**: texture thủ tục (cỏ cắt sọc, nhựa đường, sàn dốc ván gỗ, bê tông, đất bãi đáp, vạch cảnh báo), khán đài 5 bậc có mái với khán giả hoạt hình, cờ, tường lốp, cột đèn, đồi xa.
- **Ánh sáng & hậu kỳ (URP)**: nắng ấm, ánh sáng môi trường 3 tầng, sương xa; tonemapping ACES, bloom cho đèn, chỉnh màu, vignette nhẹ, motion blur, khử răng cưa SMAA, bóng đổ xa 300–450 m (tự trả lại cài đặt URP khi thoát).
- **Hiệu ứng theo sự kiện thật**: bụi khi tiếp đất/lật, tia lửa + mảnh vỡ khi va chướng ngại.
- **Khi xe chạy**: tên xe + tốc độ km/h nổi trên đầu xe (luôn quay về camera, cỡ chữ không đổi theo khoảng cách, ẩn tốc độ khi phát lại chậm), vệt đèn hậu đỏ khi > 70 km/h, bụi tung từ bánh sau khi chạy nhanh, camera bám đuôi mở rộng góc nhìn theo tốc độ.
- Chất lượng Low / Medium / High ở tab Camera.

3 xe mẫu có sẵn là thiết kế chung chung tự tạo. Xe hãng thật chỉ đến từ mẫu 3D bạn thêm (xem phần trên); `ClaimChecker` chỉ cho phép tên hãng của chính các xe đó.

## 1. Cài đặt trên Windows 11 (3 bước)

1. Unity Hub ▸ **New project** ▸ template **Universal 3D** ▸ Create.
2. Trên trang GitHub này bấm **Code ▸ Download ZIP**, giải nén, rồi chép thư mục `Assets/_MYLTI` vào thư mục `Assets` của dự án (trong Unity: cửa sổ *Project* ▸ chuột phải `Assets` ▸ **Show in Explorer**).
3. Chờ Unity biên dịch xong rồi bấm **▶ Play**. Studio tự khởi động trong bất kỳ scene nào (kể cả `SampleScene`) — không cần tạo scene.

**Ra tệp .exe (tuỳ chọn):** menu **MYLTI ▸ Build Windows x64** → thư mục chứa `MYLTI Challenge Studio.exe` tự mở ra; chép cả thư mục đó sang máy nào cũng chạy bằng cách nhấp đúp.

*(Tuỳ chọn – quay trong Editor)* **Window ▸ Package Manager ▸ + ▸ Add package by name…** → `com.unity.recorder`.

Không cần mạng, tài khoản hay API trả phí. Dữ liệu người dùng nằm tại
`%USERPROFILE%\AppData\LocalLow\MYLTI\MYLTI Challenge Studio\` (menu **MYLTI ▸ Open Data Folder**):
`Presets/`, `Runs/<runId>/`, `Vehicles/` (mẫu xe 3D), `Music/`, `Sky/`, `settings.json`, `episode_history.json`.

## 2. Dùng thử nhanh

| Bước | Làm gì |
|---|---|
| Sandbox | Tab **Thử thách** chỉnh dốc/góc/chướng ngại → **Start**. Tab **Xe** bật/tắt 3 xe, màu, AI/Manual. |
| Lái tay | Chế độ lái = Manual → **W/S** (↑/↓) ga-lùi, **A/D** lái, **Space** phanh, **R** reset. |
| Camera | **1** Chase · **2** Side (tele) · **3** Front/Lip · **4** Impact (tele) · **5** Wide · **6** Top · **7** Orbit · **8** Free (chuột phải + WASD, Q/E) · **9** Khán giả · **0** Auto |
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
- `ClaimChecker` từ chối: số không có trong fact sheet; “crash/rollover/fell off” khi không có sự kiện đó; “wins/winner” khi không có người thắng; nhãn hiệu không thuộc xe của bạn (Ferrari, BeamNG…); “realistic damage / real-world physics / scientific proof”. Câu hỏi (“Who Wins?”) được phép.
- Không có `CaptureVerified` nếu chưa đọc được tệp và thời lượng. `PublishReady` luôn cần chủ kênh duyệt.
- Bộ tạo nội dung là **template offline có điều kiện — không phải AI**. Interface `IContentGenerator` để gắn LLM sau (tuỳ chọn, kết quả vẫn bị ClaimChecker kiểm tra lại và rơi về offline nếu sai/lỗi mạng).

## 6. Giới hạn đã biết

- **Chưa chạy thử trong Unity**: cần tinh chỉnh vật lý (bán kính chuyển dốc, lò xo, ổn định trên không, AI bám làn) khi bấm Play lần đầu.
- Unity Recorder chỉ có trong Editor; bản build dùng OBS. Không có trình giải mã video: app kiểm tra container/thời lượng/track âm thanh, **không** xác nhận phát được — hãy mở tệp xem thử.
- Một bản đồ Mega Ramp (biến thể: cầu hẹp, thùng phuy, tường thùng, vòng mục tiêu, dốc tiếp đất, tường bê tông đâm thẳng, cần cẩu thả rơi). Obstacle Run riêng là P1.
- Vật lý xe vẫn là thân cứng + bánh WheelCollider; móp méo chỉ là **hình ảnh** (khối va chạm không đổi), không phải mô phỏng kết cấu như BeamNG. Mẫu xe ít đa giác sẽ móp thô; mẫu quá nặng (> 250k đỉnh mỗi phần) bỏ qua móp.
- Dò bánh xe tự động có thể sai với mẫu đặt tên lạ → xem ghi chú dưới tên xe; bánh không tìm thấy thì xe vẫn chạy nhưng bánh không quay.
- Bản .exe: shader của glTFast + skybox được thêm vào *Always Included Shaders* khi build; nếu mẫu xe hiện màu hồng trong .exe, hãy quay bằng Editor.
- Đường tải HDRI theo API công khai của Poly Haven — nếu Poly Haven đổi API, thả file .hdr thủ công vào thư mục Sky.
- Chỉ 16:9 1920×1080. 9:16, quay phân đoạn + tự dựng, timeline scrub cho replay, Analytics import, Underdog Comeback series: P1.
- Mật khẩu OBS chỉ lưu khi bạn bật “Nhớ mật khẩu” (dạng văn bản trong `settings.json` trên máy bạn).
- Giao diện IMGUI dùng font mặc định của Unity; dấu tiếng Việt dựa vào font dự phòng của Windows.

Kiến trúc, ma trận P0/P1 và hợp đồng dữ liệu: `docs/ARCHITECTURE.md`. Checklist kiểm thử: `docs/TEST_CHECKLIST.md`.
