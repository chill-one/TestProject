#!/usr/bin/env bash
# Comprehensive regression test for the File Browser take-home.
# Run from the repository root: ./test_all.sh
# Optional: PROJECT_FILE=./TestProject.csproj BASE_URL=http://127.0.0.1:5055 ./test_all.sh
# The script uses an isolated temporary FileBrowser home and does not touch your real configured home.

set -u
set -o pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:5055}"
PROJECT_FILE="${PROJECT_FILE:-}"
PASS_COUNT=0
FAIL_COUNT=0
SERVER_PID=""
TEMP_ROOT=""
SERVER_LOG=""

pass(){ PASS_COUNT=$((PASS_COUNT+1)); printf '  ✅ %s\n' "$1"; }
fail(){ FAIL_COUNT=$((FAIL_COUNT+1)); printf '  ❌ %s\n' "$1"; [ -n "${2:-}" ] && printf '     %s\n' "$2"; }
section(){ printf '\n============================================================\n%s\n============================================================\n' "$1"; }

cleanup(){
  if [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  [ -n "$TEMP_ROOT" ] && [ -d "$TEMP_ROOT" ] && rm -rf "$TEMP_ROOT"
}
trap cleanup EXIT INT TERM

for cmd in dotnet curl python3; do
  command -v "$cmd" >/dev/null 2>&1 || { echo "Missing required command: $cmd"; exit 2; }
done

http_status_get(){ local url="$1"; shift; curl -sS -o /dev/null -w '%{http_code}' -G "$url" "$@"; }
http_status_post_file(){ local url="$1" file="$2"; curl -sS -o /dev/null -w '%{http_code}' -F "file=@${file}" "$url"; }
assert_status(){ [ "$1" = "$2" ] && pass "$3" || fail "$3" "expected HTTP $2, got HTTP $1"; }
assert_file_content(){ local actual; actual="$(cat "$1" 2>/dev/null || true)"; [ "$actual" = "$2" ] && pass "$3" || fail "$3" "expected '$2', got '$actual'"; }
json_assert(){
  local file="$1" label="$2" body="$3"
  if python3 - "$file" "$body" <<'PY'
import json, sys
with open(sys.argv[1], encoding='utf-8') as f: data=json.load(f)
scope={'data':data}
exec(sys.argv[2], scope, scope)
PY
  then pass "$label"; else fail "$label"; fi
}

section '1. Locate project'
if [ -z "$PROJECT_FILE" ]; then
  PROJECT_FILE="$(find . -maxdepth 3 -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' | head -n 1)"
fi
if [ -z "$PROJECT_FILE" ] || [ ! -f "$PROJECT_FILE" ]; then
  echo 'Could not find a .csproj file.'
  echo 'Run with: PROJECT_FILE=./path/to/project.csproj ./test_all.sh'
  exit 2
fi
PROJECT_DIR="$(cd "$(dirname "$PROJECT_FILE")" && pwd)"
PROJECT_FILE="$PROJECT_DIR/$(basename "$PROJECT_FILE")"
echo "Project: $PROJECT_FILE"
pass 'Project file found'

section '2. Build'
if dotnet build "$PROJECT_FILE" >/tmp/filebrowser-build.log 2>&1; then
  pass 'dotnet build succeeds'
else
  fail 'dotnet build succeeds' 'See /tmp/filebrowser-build.log'
  cat /tmp/filebrowser-build.log
  exit 1
fi

section '3. Create isolated fixture'
TEMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/filebrowser-test.XXXXXX")"
SERVER_LOG="$TEMP_ROOT/server.log"
FIXTURE="$TEMP_ROOT/TakeHomeTestFixture"
mkdir -p "$FIXTURE/Documents/Notes" "$FIXTURE/EmptyFolder" "$FIXTURE/SearchArea/ReportFolder" "$FIXTURE/SearchArea/misc"
printf '1234567890' > "$FIXTURE/Documents/report.txt"              # 10
printf 'abcde'      > "$FIXTURE/Documents/Notes/note.txt"          # 5
printf 'abcd'       > "$FIXTURE/SearchArea/ReportFolder/report-a.txt" # 4
printf '123456'     > "$FIXTURE/SearchArea/ReportFolder/image.bin" # 6
printf 'xyz'        > "$FIXTURE/SearchArea/misc/REPORT-final.txt"  # 3
printf 'hello!'     > "$FIXTURE/hello.txt"                         # 6
printf 'upload!'    > "$TEMP_ROOT/upload.txt"                      # 7
: > "$TEMP_ROOT/empty.txt"
pass 'Fixture created (known total = 34 bytes)'

section '4. Start application'
(
  cd "$PROJECT_DIR" || exit 1
  FileBrowser__HomeDirectory="$TEMP_ROOT" ASPNETCORE_URLS="$BASE_URL" \
    dotnet run --project "$PROJECT_FILE" --no-build --no-launch-profile
) >"$SERVER_LOG" 2>&1 &
SERVER_PID=$!
READY=0
for _ in $(seq 1 60); do
  kill -0 "$SERVER_PID" 2>/dev/null || break
  CODE="$(curl -sS -o /dev/null -w '%{http_code}' "$BASE_URL/api/files?path=" 2>/dev/null || true)"
  [ "$CODE" = '200' ] && { READY=1; break; }
  sleep 0.5
done
if [ "$READY" -eq 1 ]; then pass 'Application starts and API is reachable'; else fail 'Application starts and API is reachable'; cat "$SERVER_LOG"; exit 1; fi

section '5. Browse API'
ROOT_JSON="$TEMP_ROOT/root.json"
curl -sS -G "$BASE_URL/api/files" --data-urlencode 'path=TakeHomeTestFixture' -o "$ROOT_JSON"
json_assert "$ROOT_JSON" 'Root browse returns 3 folders and 1 file' $'assert len(data)==4\nassert sum(x["type"]=="Directory" for x in data)==3\nassert sum(x["type"]=="File" for x in data)==1'
json_assert "$ROOT_JSON" 'Folders appear before files' $'types=[x["type"] for x in data]\nassert types == sorted(types, key=lambda t: 0 if t=="Directory" else 1)'
json_assert "$ROOT_JSON" 'Root sizes are correct' $'i={x["name"]:x for x in data}\nassert i["Documents"]["size"]==15\nassert i["EmptyFolder"]["size"]==0\nassert i["SearchArea"]["size"]==13\nassert i["hello.txt"]["size"]==6'
json_assert "$ROOT_JSON" 'Paths are relative and use forward slashes' $'assert all(not x["path"].startswith("/") for x in data)\nassert all("\\\\" not in x["path"] for x in data)'
json_assert "$ROOT_JSON" 'Every item has a parseable lastModifiedDate' $'from datetime import datetime\nfor x in data: datetime.fromisoformat(x["lastModifiedDate"].replace("Z","+00:00"))'

NESTED_JSON="$TEMP_ROOT/nested.json"
curl -sS -G "$BASE_URL/api/files" --data-urlencode 'path=TakeHomeTestFixture/Documents' -o "$NESTED_JSON"
json_assert "$NESTED_JSON" 'Nested browse returns Notes=5 and report.txt=10' $'i={x["name"]:x for x in data}\nassert set(i)=={"Notes","report.txt"}\nassert i["Notes"]["type"]=="Directory" and i["Notes"]["size"]==5\nassert i["report.txt"]["type"]=="File" and i["report.txt"]["size"]==10'
EMPTY_JSON="$TEMP_ROOT/empty.json"
curl -sS -G "$BASE_URL/api/files" --data-urlencode 'path=TakeHomeTestFixture/EmptyFolder' -o "$EMPTY_JSON"
json_assert "$EMPTY_JSON" 'Empty folder returns []' 'assert data == []'

section '6. Search API'
SEARCH_JSON="$TEMP_ROOT/search.json"
curl -sS -G "$BASE_URL/api/files/search" --data-urlencode 'path=TakeHomeTestFixture' --data-urlencode 'query=report' -o "$SEARCH_JSON"
json_assert "$SEARCH_JSON" 'Recursive search returns expected 1 folder + 3 files' $'p={x["path"]:x for x in data}\ne={"TakeHomeTestFixture/Documents/report.txt","TakeHomeTestFixture/SearchArea/ReportFolder","TakeHomeTestFixture/SearchArea/ReportFolder/report-a.txt","TakeHomeTestFixture/SearchArea/misc/REPORT-final.txt"}\nassert set(p)==e\nassert sum(x["type"]=="Directory" for x in data)==1\nassert sum(x["type"]=="File" for x in data)==3'
json_assert "$SEARCH_JSON" 'Matching directory size is 10 bytes' $'d=next(x for x in data if x["type"]=="Directory")\nassert d["size"]==10'
json_assert "$SEARCH_JSON" 'Matched file sizes total 17 bytes' 'assert sum(x["size"] for x in data if x["type"]=="File") == 17'
SEARCH_UPPER="$TEMP_ROOT/search-upper.json"
curl -sS -G "$BASE_URL/api/files/search" --data-urlencode 'path=TakeHomeTestFixture' --data-urlencode 'query=REPORT' -o "$SEARCH_UPPER"
json_assert "$SEARCH_UPPER" 'Search is case-insensitive' $'assert {x["path"] for x in data}=={"TakeHomeTestFixture/Documents/report.txt","TakeHomeTestFixture/SearchArea/ReportFolder","TakeHomeTestFixture/SearchArea/ReportFolder/report-a.txt","TakeHomeTestFixture/SearchArea/misc/REPORT-final.txt"}'
SCOPED="$TEMP_ROOT/scoped.json"
curl -sS -G "$BASE_URL/api/files/search" --data-urlencode 'path=TakeHomeTestFixture/SearchArea' --data-urlencode 'query=report' -o "$SCOPED"
json_assert "$SCOPED" 'Search stays inside requested starting directory' $'p={x["path"] for x in data}\nassert "TakeHomeTestFixture/Documents/report.txt" not in p\nassert len(p)==3'
NORESULT="$TEMP_ROOT/noresult.json"
curl -sS -G "$BASE_URL/api/files/search" --data-urlencode 'path=TakeHomeTestFixture' --data-urlencode 'query=definitely-does-not-exist' -o "$NORESULT"
json_assert "$NORESULT" 'No-result search returns []' 'assert data == []'
BLANK="$TEMP_ROOT/blank.json"
curl -sS -G "$BASE_URL/api/files/search" --data-urlencode 'path=TakeHomeTestFixture' --data-urlencode 'query=' -o "$BLANK"
json_assert "$BLANK" 'Blank query on valid path returns []' 'assert data == []'

section '7. Download'
DOWNLOAD="$TEMP_ROOT/downloaded.txt"
STATUS="$(curl -sS -o "$DOWNLOAD" -w '%{http_code}' -G "$BASE_URL/api/files/download" --data-urlencode 'path=TakeHomeTestFixture/hello.txt')"
assert_status "$STATUS" 200 'Existing file downloads'
assert_file_content "$DOWNLOAD" 'hello!' 'Downloaded content is exact'
assert_status "$(http_status_get "$BASE_URL/api/files/download" --data-urlencode 'path=TakeHomeTestFixture/nope.txt')" 404 'Missing download returns 404'
assert_status "$(http_status_get "$BASE_URL/api/files/download" --data-urlencode 'path=TakeHomeTestFixture/Documents')" 404 'Downloading a directory returns 404'

section '8. Upload'
assert_status "$(http_status_post_file "$BASE_URL/api/files/upload?path=TakeHomeTestFixture" "$TEMP_ROOT/upload.txt")" 200 'Upload succeeds'
AFTER_UPLOAD="$TEMP_ROOT/after-upload.json"
curl -sS -G "$BASE_URL/api/files" --data-urlencode 'path=TakeHomeTestFixture' -o "$AFTER_UPLOAD"
json_assert "$AFTER_UPLOAD" 'Uploaded file appears and browse total becomes 41 bytes' $'i={x["name"]:x for x in data}\nassert i["upload.txt"]["size"]==7\nassert sum(x["size"] or 0 for x in data)==41'
assert_status "$(http_status_post_file "$BASE_URL/api/files/upload?path=TakeHomeTestFixture" "$TEMP_ROOT/upload.txt")" 409 'Duplicate upload returns 409'
assert_status "$(http_status_post_file "$BASE_URL/api/files/upload?path=TakeHomeTestFixture" "$TEMP_ROOT/empty.txt")" 400 'Zero-byte upload returns 400'
assert_status "$(http_status_post_file "$BASE_URL/api/files/upload?path=NoSuchFolder" "$TEMP_ROOT/upload.txt")" 404 'Upload to missing directory returns 404'
SANITIZE="$(curl -sS -o /dev/null -w '%{http_code}' -F "file=@$TEMP_ROOT/upload.txt;filename=../escape.txt" "$BASE_URL/api/files/upload?path=TakeHomeTestFixture")"
assert_status "$SANITIZE" 200 'Filename traversal is sanitized'
if [ -f "$FIXTURE/escape.txt" ] && [ ! -f "$TEMP_ROOT/escape.txt" ]; then pass 'Sanitized upload stays inside destination'; else fail 'Sanitized upload stays inside destination'; fi

section '9. Error handling and root restriction'
assert_status "$(http_status_get "$BASE_URL/api/files" --data-urlencode 'path=DoesNotExist')" 404 'Missing browse directory returns 404'
assert_status "$(http_status_get "$BASE_URL/api/files/search" --data-urlencode 'path=DoesNotExist' --data-urlencode 'query=')" 404 'Invalid search directory is checked before blank-query shortcut'
assert_status "$(http_status_get "$BASE_URL/api/files" --data-urlencode 'path=../../')" 403 'Browse traversal returns 403'
assert_status "$(http_status_get "$BASE_URL/api/files/search" --data-urlencode 'path=../../' --data-urlencode 'query=x')" 403 'Search traversal returns 403'
assert_status "$(http_status_get "$BASE_URL/api/files/download" --data-urlencode 'path=../../etc/passwd')" 403 'Download traversal returns 403'
assert_status "$(http_status_post_file "$BASE_URL/api/files/upload?path=../../" "$TEMP_ROOT/upload.txt")" 403 'Upload traversal returns 403'
assert_status "$(http_status_get "$BASE_URL/api/files" --data-urlencode "path=../$(basename "$TEMP_ROOT")Evil")" 403 'Similar-prefix path cannot escape root'

section '10. Spaces and long names'
mkdir -p "$FIXTURE/My Folder"
printf 'space' > "$FIXTURE/My Folder/my file.txt"
SPACE_JSON="$TEMP_ROOT/space.json"
curl -sS -G "$BASE_URL/api/files" --data-urlencode 'path=TakeHomeTestFixture/My Folder' -o "$SPACE_JSON"
json_assert "$SPACE_JSON" 'Path with spaces browses correctly' $'assert len(data)==1\nassert data[0]["name"]=="my file.txt" and data[0]["size"]==5'
SPACE_DL="$TEMP_ROOT/space-download.txt"
assert_status "$(curl -sS -o "$SPACE_DL" -w '%{http_code}' -G "$BASE_URL/api/files/download" --data-urlencode 'path=TakeHomeTestFixture/My Folder/my file.txt')" 200 'Spaced filename downloads'
assert_file_content "$SPACE_DL" 'space' 'Spaced filename content is correct'
LONG='this-is-a-very-long-filename-used-to-test-the-file-browser-layout-and-make-sure-it-does-not-break.txt'
printf 'test' > "$FIXTURE/$LONG"
LONG_JSON="$TEMP_ROOT/long.json"
curl -sS -G "$BASE_URL/api/files" --data-urlencode 'path=TakeHomeTestFixture' -o "$LONG_JSON"
json_assert "$LONG_JSON" 'Long filename is returned intact' 'assert any(x["name"]=="this-is-a-very-long-filename-used-to-test-the-file-browser-layout-and-make-sure-it-does-not-break.txt" for x in data)'

section '11. Symlink/reparse-point enumeration'
if ln -s /tmp "$FIXTURE/outside-link" 2>/dev/null; then
  SYM="$TEMP_ROOT/sym.json"
  curl -sS -G "$BASE_URL/api/files" --data-urlencode 'path=TakeHomeTestFixture' -o "$SYM"
  json_assert "$SYM" 'Browse skips symlink/reparse-point entry' 'assert all(x["name"] != "outside-link" for x in data)'
  rm -f "$FIXTURE/outside-link"
else
  echo '  ⚠️  Could not create symlink; skipped this check.'
fi

section '12. Frontend static checks'
INDEX="$PROJECT_DIR/wwwroot/index.html"
APP="$PROJECT_DIR/wwwroot/app.js"
STYLE="$PROJECT_DIR/wwwroot/styles.css"
[ -f "$INDEX" ] && pass 'wwwroot/index.html exists' || fail 'wwwroot/index.html exists'
[ -f "$APP" ] && pass 'wwwroot/app.js exists' || fail 'wwwroot/app.js exists'
[ -f "$STYLE" ] && pass 'wwwroot/styles.css exists' || fail 'wwwroot/styles.css exists'
if [ -f "$INDEX" ] && grep -q 'id="search-form"' "$INDEX" && grep -q 'id="upload-form"' "$INDEX" && grep -q 'id="file-list"' "$INDEX"; then pass 'HTML contains search/upload/file-list UI'; else fail 'HTML contains search/upload/file-list UI'; fi
if [ -f "$INDEX" ] && grep -q 'styles.css' "$INDEX"; then pass 'HTML links styles.css'; else fail 'HTML links styles.css'; fi
if [ -f "$APP" ]; then
  grep -q 'AbortController' "$APP" && grep -q 'cancelActiveSearch' "$APP" && pass 'Frontend contains AbortController cancellation' || fail 'Frontend contains AbortController cancellation'
  grep -q 'popstate' "$APP" && grep -q 'pushState' "$APP" && pass 'Frontend contains history/deep-link logic' || fail 'Frontend contains history/deep-link logic'
  grep -q 'appendItemDetails' "$APP" && grep -q 'createTextSpan' "$APP" && pass 'Frontend contains shared row helpers' || fail 'Frontend contains shared row helpers'
  grep -q 'toLocaleString' "$APP" && pass 'Frontend formats local date + time' || fail 'Frontend formats local date + time' 'Expected formatDate() to use toLocaleString().'
  if python3 - "$APP" <<'PY'
import re, sys
text=open(sys.argv[1],encoding='utf-8').read()
m=re.search(r'async\s+function\s+loadFiles\s*\([^)]*\)\s*\{(.*?)\n\}', text, re.S)
assert m and 'cancelActiveSearch()' in m.group(1)
PY
  then pass 'loadFiles() cancels active search before browsing'; else fail 'loadFiles() cancels active search before browsing'; fi
  if command -v node >/dev/null 2>&1; then node --check "$APP" >/dev/null 2>&1 && pass 'app.js passes syntax check' || fail 'app.js passes syntax check'; else echo '  ⚠️  Node not installed; skipped JS syntax check.'; fi
fi

section '13. SPA route serving'
assert_status "$(curl -sS -o /dev/null -w '%{http_code}' "$BASE_URL/")" 200 'SPA root serves'
assert_status "$(curl -sS -o /dev/null -w '%{http_code}' "$BASE_URL/?path=TakeHomeTestFixture%2FDocuments%2FNotes")" 200 'Folder deep-link URL serves SPA'
assert_status "$(curl -sS -o /dev/null -w '%{http_code}' "$BASE_URL/?path=TakeHomeTestFixture&search=report")" 200 'Search deep-link URL serves SPA'

section '14. Manual browser checks'
cat <<'CHECKLIST'
The automated checks above cover the backend/API and static SPA wiring. These
still need a real browser (unless you later add Playwright/Cypress):

  [ ] Fresh fixture browse shows: 3 folders • 1 file • Total size: 34 bytes
  [ ] Search "report" shows: 1 folder • 3 files • Matched file size: 17 bytes
  [ ] Clicking ReportFolder from search works and its size is not null
  [ ] Modified DATE + TIME are visible
  [ ] Refreshing a nested folder URL restores that folder
  [ ] Refreshing a search URL restores input + results
  [ ] Browser Back/Forward restores correct states
  [ ] Breadcrumbs navigate correctly
  [ ] Start search A then B quickly: only B remains
  [ ] Start search then navigate: stale search never overwrites browse results
  [ ] Cancellation never displays AbortError
  [ ] Browser upload refreshes rows/summary and clears file input
  [ ] Browser download works
  [ ] Long filename does not break row/grid layout
  [ ] Singular labels say "1 folder" / "1 file"
CHECKLIST

section 'RESULT'
printf 'Passed: %d\nFailed: %d\n' "$PASS_COUNT" "$FAIL_COUNT"
if [ "$FAIL_COUNT" -eq 0 ]; then
  echo '✅ All automated checks passed. Finish the manual-browser checklist before submission.'
  exit 0
else
  echo '❌ Some automated checks failed. Review the failures above.'
  echo "Server log was: $SERVER_LOG"
  exit 1
fi
