# OCR Runtime Evaluation

Status: runtime candidate evaluated; not accepted for the base application.

## Candidate

- Engine: Tesseract OCR 5.5.1.
- .NET MAUI binding: `TesseractOcrMaui` 1.5.2.
- Targets documented by the package: .NET 9+, Android (ARM64, ARMv7, x86, x64) and Windows x64.
- Persian language data: official `fas.traineddata` from `tessdata_fast`, approximately 421 KB.
- Licensing: the binding and the official language-data repository declare Apache-2.0. Tesseract itself is Apache-2.0; native dependencies have separate licenses and require a complete notice audit before redistribution.

## Size assessment

The NuGet package download is 76.33 MB. Its published Android ARM64 native assets include:

| Native library | Size |
|---|---:|
| `libtesseract.so` | 46,369,960 bytes |
| `libleptonica.so` | 9,645,576 bytes |
| `libjpeg.so` | 3,268,128 bytes |
| `libtiff.so` | 1,955,944 bytes |
| `libpng16.so` | 1,057,064 bytes |

The language model is small; the native runtime is the dominant size cost. Adding this package directly to the main MAUI app would introduce a substantial Android binary-size increase. The package was briefly evaluated in the branch and then removed before acceptance; no OCR dependency or runtime is currently part of the app.

## Decision

Do not ship this runtime in the base app.

A separate optional runtime remains a possible path, but requires a dedicated proof of concept for:
- downloading or importing a platform-specific runtime package without uploading receipt images or recognized text;
- integrity verification before installation;
- loading native libraries from app-private storage on Android and Windows;
- uninstalling/updating the optional runtime safely;
- measuring actual installed size and recognition performance;
- completing license notices for Tesseract, Leptonica, JPEG, PNG, TIFF, zlib and the binding.

The `tessdata_fast` documentation describes its models as faster and smaller than `tessdata_best`, with a speed/accuracy trade-off. The Persian model's small size does not solve the native runtime-size issue.

Until the optional-runtime proof of concept passes, OCR is not implemented and must not be described as available. A manual, user-reviewed transaction draft remains the fallback. No OCR result may post to the ledger without explicit user confirmation.

## Sources

- [TesseractOcrMaui 1.5.2 package, supported targets, API, license and package size](https://www.nuget.org/packages/TesseractOcrMaui/1.5.2)
- [TesseractOcrMaui v1.5.2 source tree and native library assets](https://github.com/henrivain/TesseractOcrMaui/tree/v1.5.2)
- [Official Persian fast traineddata file](https://github.com/tesseract-ocr/tessdata_fast/blob/main/fas.traineddata)
- [Tesseract trained-data variants and speed/accuracy trade-offs](https://tesseract-ocr.github.io/tessdoc/Data-Files-in-tessdata_fast.html)
- [Tesseract license and supported platforms](https://github.com/tesseract-ocr/tesseract)
