"""Verify packaged files and captured evidence without opening/mutating scenes."""
from html.parser import HTMLParser
from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Captures/Rib2HandsReview_V02_20261027"


class Links(HTMLParser):
    def __init__(self):
        super().__init__()
        self.paths = []

    def handle_starttag(self, tag, attrs):
        self.paths.extend(value for key, value in attrs if key in ("href", "src") and value)


def main():
    report_path = OUT / "render_20261027_101420_188/receipt.json"
    report = json.loads(report_path.read_text(encoding="utf-8"))
    keep = json.loads((OUT / "preservation_verified.json").read_text(encoding="utf-8"))
    links = Links()
    links.feed((OUT / "review.html").read_text(encoding="utf-8"))
    assert all((OUT / path).is_file() for path in links.paths)
    assert report["success"] and keep["success"]
    current_implementation = hashlib.sha256((ROOT / "Assets/Editor/Rib2HandsVideoReviewWindow.cs").read_bytes()).hexdigest()
    assert current_implementation == report["implementationSha256"]
    build = (ROOT / "Temp/rib2-hands-review-editor-build.log").read_text(encoding="utf-8")
    assert "Rib2HandsVideoReviewWindow.cs(" not in build
    assert "오류 0개" in build or "0 Error(s)" in build
    (OUT / "build.txt").write_text(build, encoding="utf-8")
    result = {
        "success": True, "capturedFrames": report["framesWritten"],
        "sourceFrames": report["sourceFrameCount"], "jointsPerHand": report["jointsPerHand"],
        "mainSceneStateUnchanged": report["originalSceneStateUnchanged"],
        "protectedFilesUnchanged": keep["protectedFiles"],
        "renderReceipt": report_path.relative_to(OUT).as_posix(),
        "captureImplementationHashMatches": True, "newScriptWarnings": 0,
        "clinicalOrPlayValidation": False, "htmlLocalLinksValid": len(links.paths),
        "minimumHandJointViewportMargin": report["minimumHandViewportMargin"],
    }
    (OUT / "verification.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()
