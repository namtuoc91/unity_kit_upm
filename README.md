# unity_kit_upm

Repo dev UPM package `com.raccoon.game-kit` (Unity 6). Source package: `unity_kit_upm/Assets/RaccoonKit/` — xem [README của package](unity_kit_upm/Assets/RaccoonKit/README.md).

## Import vào project khác
Package Manager → `+` → **Add package from git URL...**

```
https://github.com/namtuoc91/unity_kit_upm.git#v0.0.1   # pin version (khuyên dùng)
https://github.com/namtuoc91/unity_kit_upm.git#upm      # luôn bản mới nhất
```

## Release (GitHub Action `UPM Release`)
1. Bump `version` trong `unity_kit_upm/Assets/RaccoonKit/package.json` + ghi `CHANGELOG.md`.
2. Commit đầy đủ file `.meta` (mở Unity cho nó sinh meta trước khi commit).
3. Push lên `main` → action tự chạy:
   - kiểm tra SemVer + đủ `.meta`,
   - tạo commit từ thư mục package (bỏ `Tests/`) → push branch `upm`,
   - tạo tag `v<version>` (tag đã có thì bỏ qua, chỉ cập nhật `upm`).
4. Chạy tay: tab **Actions → UPM Release → Run workflow** (`force_retag` = tạo lại tag đã có).

> Không commit trực tiếp vào branch `upm` — branch này được sinh tự động.
