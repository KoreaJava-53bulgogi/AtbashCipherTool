// atbash-vision-ocr: macOS 의 Apple Vision 으로 그림 속 글자(한국어·영어)를 읽어 표준 출력(UTF-8)으로 내보냄.
// 앱(.app/Contents/MacOS)에 함께 들어가고, C# 의 VisionOcrEngine 이 실행함.
//
//   atbash-vision-ocr <그림 파일>
//
// 끝 코드: 0 성공(글자가 없으면 빈 출력), 2 사용법 오류, 3 그림을 읽을 수 없음, 4 인식 실패
// 빌드: swiftc -O -target arm64-apple-macos13.0 VisionOcr.swift -o atbash-vision-ocr
import CoreGraphics
import Foundation
import ImageIO
import Vision

func fail(_ message: String, _ code: Int32) -> Never {
    FileHandle.standardError.write((message + "\n").data(using: .utf8)!)
    exit(code)
}

let arguments = CommandLine.arguments
guard arguments.count == 2 else { fail("usage: atbash-vision-ocr <image>", 2) }

let url = URL(fileURLWithPath: arguments[1])
guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
      let image = CGImageSourceCreateImageAtIndex(source, 0, nil)
else { fail("cannot read image: \(arguments[1])", 3) }

let request = VNRecognizeTextRequest()
request.recognitionLevel = .accurate
request.usesLanguageCorrection = true
request.revision = VNRecognizeTextRequestRevision3 // 한국어는 revision 3 (macOS 13+) 부터
request.recognitionLanguages = ["ko-KR", "en-US"]

do {
    try VNImageRequestHandler(cgImage: image, options: [:]).perform([request])
} catch {
    fail("recognition failed: \(error.localizedDescription)", 4)
}

// 결과는 순서가 보장되지 않으므로 위에서 아래, 왼쪽에서 오른쪽으로 정렬.
// Vision 좌표는 왼쪽 아래가 원점(0~1) → y 가 클수록 위쪽. 높이의 절반 안쪽 차이는 같은 줄로 봄.
struct Line { let text: String; let box: CGRect }
let lines = (request.results ?? []).compactMap { observation -> Line? in
    guard let best = observation.topCandidates(1).first else { return nil }
    return Line(text: best.string, box: observation.boundingBox)
}
var rows: [[Line]] = []
for line in lines.sorted(by: { $0.box.midY > $1.box.midY }) {
    if let first = rows.last?.first, abs(first.box.midY - line.box.midY) < min(first.box.height, line.box.height) / 2 {
        rows[rows.count - 1].append(line)
    } else {
        rows.append([line])
    }
}
let text = rows.map { row in row.sorted { $0.box.minX < $1.box.minX }.map(\.text).joined(separator: " ") }
print(text.joined(separator: "\n"))
