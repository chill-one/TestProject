# Design Choices

## Overview

This project is a lightweight single-page file browser built with ASP.NET Core and vanilla JavaScript.

The application exposes a configurable server-side home directory and allows the user to:

- Browse files and directories.
- Recursively search by file or directory name.
- View file and folder sizes.
- View last-modified dates and times.
- Upload files.
- Download files.
- Navigate with breadcrumbs.
- Use browser Back and Forward.
- Deep-link directly to a directory or search state.

The backend returns JSON and the frontend is responsible for rendering the UI.

---

# FileItem Model

Each file or directory is represented by a `FileItem`.

```text
FileItem
- name
- path
- type
- size
- lastModifiedDate
```

## `name`

Type: `string`

Represents the display name of the file or directory.

Example:

```text
report.pdf
Projects
```

## `path`

Type: `string`

Represents the path relative to the configured home directory.

The API does not send absolute filesystem paths to the browser.

Example:

```text
Server path:
/Users/Someone/Desktop/CompanyProject/files/Documents/report.pdf

Client path:
Documents/report.pdf
```

### Why relative paths?

Using relative paths:

- Keeps the server's actual filesystem layout hidden.
- Works naturally with a configurable home directory.
- Makes browser navigation independent of the machine where the server is running.
- Makes it easier to deep-link using URL query parameters.

The backend also normalizes client paths to use `/`, regardless of whether the server runs on Windows, macOS, or Linux.

## `type`

Type: `FileItemType`

```csharp
File
Directory
```

An enum is used instead of arbitrary strings because the value should come from a controlled set.

This avoids inconsistencies such as:

```text
file
File
folder
Folder
directory
```

## `size`

Type: `long?`

A 32-bit integer is not large enough for many files or directories, so a 64-bit `long` is used.

For files:

```text
size = FileInfo.Length
```

For directories:

```text
size = total bytes of all files recursively contained in the directory
```

`null` is reserved for situations where a size is unavailable or intentionally not calculated.

A real empty file or empty directory should use:

```text
0
```

not `null`.

## `lastModifiedDate`

Type: `DateTimeOffset`

The server uses the filesystem's last-write time and sends it to the browser.

The browser formats the timestamp for the user's local timezone.

`DateTimeOffset` is preferable to a plain formatted string because the API can transfer an actual timestamp and leave display formatting to the client.

---

# Configurable Home Directory

The browser is restricted to one configured root directory.

Example configuration:

```json
{
  "FileBrowser": {
    "HomeDirectory": "/Users/Someone/TestFiles"
  }
}
```

The application validates this directory at startup.

If the configured directory does not exist, the application fails immediately rather than starting in a broken state.

This is preferable to discovering the configuration problem only after the first API request.

---

# Filesystem Path Security

## Problem

Every path coming from the browser must be treated as untrusted input.

Suppose the configured root is:

```text
/Users/Name/TestFiles
```

A user could attempt to send:

```text
../../../../etc
```

If the server used that path directly, files outside the intended root could be exposed.

## ResolvePath

All browser-provided paths go through `ResolvePath()`.

The process is:

```text
configured home directory
        ↓
combine with relative browser path
        ↓
Path.GetFullPath(...)
        ↓
verify normalized result is still inside home directory
        ↓
allow or reject
```

The core operations are:

```csharp
Path.Combine(...)
Path.GetFullPath(...)
```

## Why a trailing directory separator matters

A naive prefix check can be dangerous.

Allowed root:

```text
/Users/Name/TestFiles
```

Potential outside path:

```text
/Users/Name/TestFilesSecret/report.pdf
```

A naive string prefix check may consider the second path to begin with the first.

To prevent this, the configured root is normalized to include its ending directory separator:

```text
/Users/Name/TestFiles/
```

Now:

```text
/Users/Name/TestFilesSecret/
```

does not begin with:

```text
/Users/Name/TestFiles/
```

## Cross-platform comparison

Filesystem path comparison differs by operating system.

The implementation uses:

```csharp
OperatingSystem.IsWindows()
    ? StringComparison.OrdinalIgnoreCase
    : StringComparison.Ordinal;
```

Windows paths are generally case-insensitive, while Unix-like systems are generally case-sensitive.

## Root itself must remain valid

The empty relative path:

```text
""
```

represents the configured root itself.

The validation therefore accepts:

```text
root itself
anything underneath root
```

and rejects:

```text
anything outside root
```

Example:

```text
Home:
    /Users/Name/TestFiles

Input:
    Documents/../Projects

Normalized:
    /Users/Name/TestFiles/Projects

Result:
    allowed
```

Example:

```text
Input:
    ../../etc

Normalized:
    /Users/etc

Result:
    rejected
```

## Symbolic links / reparse points

Normal directory enumeration skips reparse points:

```csharp
AttributesToSkip = FileAttributes.ReparsePoint
```

This prevents ordinary browse/search traversal through symlink-like filesystem entries.

A production-grade sandbox could go further by resolving link targets before permitting direct access through a known link path.

---

# Filesystem Enumeration Policy

Several operations need to enumerate files and directories.

The common policy is represented by:

```csharp
CreateEnumerationOptions(bool recursive)
```

The options are:

```csharp
new EnumerationOptions
{
    RecurseSubdirectories = recursive,
    IgnoreInaccessible = true,
    AttributesToSkip = FileAttributes.ReparsePoint
};
```

## Why `IgnoreInaccessible`?

A filesystem can contain directories that the process cannot read.

Without this option, a browse or recursive search could fail completely when one inaccessible directory is encountered.

## Why skip `ReparsePoint`?

This prevents normal enumeration from following symbolic-link/junction-like entries.

---

# Browsing

## Goal

Browse should return the direct children of the current directory.

Example:

```text
Projects/
├── API/
├── Frontend/
└── README.md
```

Request:

```http
GET /api/files?path=Projects
```

The response should include:

```text
API
Frontend
README.md
```

but should not recursively return every descendant.

## Implementation

The backend constructs a `DirectoryInfo` for the resolved path and performs one non-recursive filesystem enumeration:

```csharp
directory.EnumerateFileSystemInfos("*", options)
```

where:

```csharp
RecurseSubdirectories = false
```

Each returned `FileSystemInfo` is classified as either:

```csharp
DirectoryInfo
FileInfo
```

Directories and files are collected separately so directories can be returned before files.

## Mapping filesystem objects

Reusable helpers convert filesystem objects into API models:

```csharp
CreateFileItem(FileInfo file)
CreateDirectoryItem(DirectoryInfo directory, long size)
```

This prevents duplicate `FileItem` construction logic across browse and search.

---

# File and Folder Sizes

## File size

File size is simple:

```csharp
file.Length
```

## Directory size

A directory does not expose a single `Length` property.

Its size must be derived from the files contained beneath it.

The implementation recursively enumerates files:

```csharp
directory.EnumerateFiles("*", options)
```

and accumulates:

```csharp
totalSize += file.Length;
```

## Browse behavior

When browsing a directory, each visible child directory receives a recursive size.

Example:

```text
Home/
├── Projects/      100 MB
├── Documents/      20 MB
└── hello.txt         1 MB
```

The current view total is:

```text
100 MB + 20 MB + 1 MB = 121 MB
```

Because the immediate child directories represent separate subtrees, their recursive sizes do not overlap.

## Performance tradeoff

Recursively computing folder sizes can be expensive for very large directory trees.

This project chooses correctness and useful information for the proof of concept while keeping the implementation understandable.

Possible future improvements include:

- Lazy folder-size calculation.
- Background calculation.
- Cached directory-size metadata.
- Filesystem change tracking.

Caching is intentionally not included because it introduces invalidation complexity when files change outside the application.

---

# Recursive Search

## Search scope

Search is relative to the current directory.

If the user is inside:

```text
Projects/
```

and searches:

```text
report
```

the application recursively searches everything below `Projects`.

It does not search outside the current directory.

## Why recursive search?

A recursive search gives the user a useful way to find files or directories that may be deeply nested.

The result path also lets the user navigate directly to a matching directory.

## Matching rule

The application uses case-insensitive substring matching:

```csharp
item.Name.Contains(
    query,
    StringComparison.OrdinalIgnoreCase
)
```

So all of these match:

```text
report
REPORT
RePoRt
```

## Single filesystem traversal

The final search implementation performs one recursive traversal:

```csharp
directory.EnumerateFileSystemInfos("*", options)
```

with:

```csharp
RecurseSubdirectories = true
```

This is preferable to separately traversing:

```text
all files
all directories
```

because the filesystem only needs to be enumerated once.

---

# Search Directory Size Optimization

A matching directory needs a size.

The naive approach would be:

```text
search recursively
        ↓
match directory A
        ↓
recursively calculate A size

match nested directory B
        ↓
recursively calculate B size again
```

This can cause overlapping filesystem scans.

Instead, folder sizes are accumulated during the same search traversal.

## Accumulation idea

Suppose:

```text
Projects/
└── API/
    └── src/
        └── report.pdf   5 MB
```

When `report.pdf` is encountered, its 5 MB contributes to:

```text
src      += 5 MB
API      += 5 MB
Projects += 5 MB
```

A dictionary stores:

```text
directory full path → accumulated size
```

Example:

```text
src      = 5 MB
API      = 5 MB
Projects = 5 MB
```

If another 2 MB file exists directly under API:

```text
API      = 7 MB
Projects = 7 MB
```

## Search root boundary

The ancestor walk stops at the directory where the search began.

If the search starts at:

```text
Projects/
```

the algorithm does not continue accumulating into:

```text
TestFiles/
Users/
/
```

The normalized search root acts as the stopping boundary.

## Matching directories

A directory may be encountered before all files inside it have been enumerated.

For this reason, matching directories are remembered first:

```text
matchingDirectories
```

After the recursive traversal finishes, all directory totals are known and the matching directory `FileItem` objects can be created.

## Complexity

The filesystem is enumerated once.

Each file also walks upward through its ancestor directories until the search root.

If:

```text
F = number of files
D = average nesting depth
```

the in-memory size bookkeeping is approximately:

```text
O(F × D)
```

In typical directory trees, `D` is small.

This avoids additional recursive filesystem I/O for every matching folder.

---

# Request Cancellation

Recursive filesystem operations can take noticeable time.

A search that is no longer needed should stop instead of continuing to consume resources.

## Backend cancellation

ASP.NET provides a request cancellation token.

The controller passes it into:

```csharp
SearchDirectory(...)
BrowseDirectory(...)
```

Long-running loops periodically call:

```csharp
cancellationToken.ThrowIfCancellationRequested();
```

Cancellation is checked during:

- Recursive search enumeration.
- Directory-size calculation.
- Processing of matching search directories.
- Browse processing.

The service does not catch `OperationCanceledException` and return partial results.

Cancellation is allowed to propagate naturally.

## Frontend cancellation

The frontend uses:

```javascript
AbortController
```

The active search controller is stored in:

```javascript
activeSearchController
```

Before a new search starts, the previous search is aborted.

Browsing also cancels an active search.

The flow is:

```text
User starts Search A
        ↓
Search A fetch is active
        ↓
User starts Search B
        ↓
Search A AbortController.abort()
        ↓
HTTP request is aborted
        ↓
ASP.NET RequestAborted
        ↓
CancellationToken
        ↓
filesystem operation stops
```

The same thing happens if the user navigates to a directory while a search is running.

## Preventing stale UI

An older search should never overwrite a newer state.

The frontend checks that a controller is still the active controller before clearing it in `finally`.

Canceled requests are treated as normal behavior, so `AbortError` is not displayed to the user.

---

# HTTP Error Handling

The service layer throws meaningful exceptions.

The controller translates those exceptions into HTTP responses.

Examples:

```text
DirectoryNotFoundException
    → 404 Not Found

FileNotFoundException
    → 404 Not Found

UnauthorizedAccessException
    → 403 Forbidden

Invalid upload
    → 400 Bad Request

Duplicate upload
    → 409 Conflict
```

This gives the frontend meaningful API behavior instead of exposing raw backend exceptions.

---

# Frontend and Static Files

The server returns JSON for API requests.

The frontend is a vanilla JavaScript single-page application.

ASP.NET static-file middleware exposes files from `wwwroot`.

The application uses:

```csharp
app.UseDefaultFiles();
app.UseStaticFiles();
```

`UseDefaultFiles()` allows `/` to resolve to the default document such as `index.html`.

`UseStaticFiles()` actually serves the files.

The frontend contains:

```text
wwwroot/
├── index.html
├── app.js
└── styles.css
```

---

# Rendering File Items

The frontend receives JSON `FileItem` objects and creates DOM elements.

Common columns are:

```text
Name
Type
Size
Modified
```

A small helper:

```javascript
createTextSpan(text)
```

creates reusable text spans.

Another helper:

```javascript
appendItemDetails(listItem, item, displayName)
```

adds the common columns to both files and directories.

`renderItems()` then focuses on behavior differences:

```text
Directory
    → navigate on click

File
    → show Download button
```

---

# Last Modified Date and Time

The backend sends the timestamp.

The browser formats it using the user's locale.

Example:

```javascript
new Date(item.lastModifiedDate).toLocaleString()
```

This keeps transport and presentation separate:

```text
backend
    → timestamp

frontend
    → local display format
```

---

# Folder Navigation

Directories are clickable.

The flow is:

```text
Click Projects
        ↓
navigateTo("Projects")
        ↓
update URL
        ↓
GET /api/files?path=Projects
        ↓
render Projects contents
```

Paths are URL-encoded before being sent to the API.

This is necessary because file paths may contain:

```text
spaces
&
?
#
other special characters
```

---

# Browser-Friendly Paths

The backend may run on:

```text
Windows
macOS
Linux
```

Filesystem separators can differ.

The client should not need to know which operating system the server uses.

The backend therefore:

1. Computes the path relative to the configured home directory.
2. Converts the platform-specific separator to `/`.

Example:

```text
Windows server:
Projects\API\src

Client:
Projects/API/src
```

Breadcrumb logic can therefore consistently use:

```javascript
path.split("/")
```

---

# Deep Linking

The browser URL is the source of truth for the current UI state.

Directory state:

```text
/?path=Projects
```

Nested directory:

```text
/?path=Projects%2FPulseWatch
```

Search state:

```text
/?path=Projects&search=report
```

## navigateTo

Directory navigation updates the `path` query parameter and removes any active search parameter.

The application uses:

```javascript
history.pushState(...)
```

so the URL changes without a full page reload.

## navigateSearch

Search updates:

```text
path
search
```

in the URL.

## Page refresh

At startup, the frontend reads:

```javascript
new URLSearchParams(window.location.search)
```

If a search parameter exists, it restores the search.

Otherwise, it restores the directory browse state.

This allows a user to:

- Refresh a nested folder.
- Refresh search results.
- Copy the current URL.
- Open the same state in another tab.

---

# Browser Back and Forward

`history.pushState()` changes browser history, but it does not automatically rerender the SPA when the user presses Back or Forward.

The frontend listens for:

```javascript
window.addEventListener("popstate", ...)
```

When the history changes, the application rereads:

```text
path
search
```

from the URL and restores the appropriate state.

This keeps:

```text
URL
visible directory/search state
```

synchronized.

---

# Breadcrumb Navigation

Breadcrumbs represent the current relative path.

Example:

```text
Home > Projects > PulseWatch > backend
```

The client splits normalized paths using:

```javascript
path.split("/")
```

It then constructs cumulative paths:

```text
Projects
Projects/PulseWatch
Projects/PulseWatch/backend
```

Each breadcrumb button navigates to its respective directory.

---

# Search UI

The search form:

1. Prevents the default page refresh.
2. Reads the query.
3. Reads the current directory from the URL.
4. Updates URL state.
5. Calls the search API.
6. Renders results.

Search results display the full relative path instead of only the basename because multiple matches may have the same filename in different directories.

Example:

```text
Documents/report.txt
Projects/API/report.txt
Archive/report.txt
```

---

# Browse Summary

The current browse view displays:

```text
folder count
file count
total size
```

Example:

```text
3 folders • 2 files • Total size: 41 bytes
```

The frontend handles singular/plural labels:

```text
1 folder
2 folders

1 file
2 files
```

---

# Search Summary

Search uses a different size meaning.

A matching directory's recursive size may already contain matching files beneath it.

If the summary added both:

```text
matching directory size
matching file size
```

the same bytes could be counted more than once.

Therefore search summary reports:

```text
folder count
file count
matched file size
```

Example:

```text
1 folder • 3 files • Matched file size: 17 bytes
```

Directory sizes are still displayed on individual directory search results.

---

# Download

## Flow

```text
User clicks Download
        ↓
relative file path
        ↓
GET /api/files/download?path=...
        ↓
FileController
        ↓
FileService.OpenDownload()
        ↓
ResolvePath()
        ↓
File.OpenRead()
        ↓
ASP.NET streams response
        ↓
browser downloads file
```

## Why stream?

Using a `FileStream` avoids loading the entire file into server memory.

This is more appropriate for larger files.

The browser also receives the original filename for the download response.

---

# Upload

## Flow

```text
User selects file
        ↓
frontend creates FormData
        ↓
POST /api/files/upload?path=current-directory
        ↓
FileController
        ↓
FileService.UploadFile()
        ↓
ResolvePath(destination directory)
        ↓
sanitize filename
        ↓
create file
        ↓
copy upload stream
        ↓
refresh current directory
```

## Filename security

The uploaded filename is untrusted.

A filename such as:

```text
../../evil.txt
```

must not be allowed to escape the chosen destination.

The backend sanitizes it using:

```csharp
Path.GetFileName(fileName)
```

Example:

```text
../../evil.txt
    ↓
evil.txt
```

## Duplicate uploads

The application rejects duplicate filenames instead of silently overwriting existing files.

A naive pattern would be:

```text
File.Exists()
        ↓
if false, create file
```

This contains a race condition.

Two requests could both observe:

```text
file does not exist
```

before either creates it.

The implementation instead creates the destination using:

```csharp
FileMode.CreateNew
```

This makes duplicate protection atomic at file creation time.

A duplicate upload is returned as:

```text
409 Conflict
```

---

# Status and Error Messages

The frontend has a dedicated status area.

Helpers:

```javascript
showStatus(message)
clearStatus()
```

are used for:

- Search progress.
- API failures.
- Upload failures.

Canceled searches are intentionally ignored and are not presented as user-facing errors.

---

# Styling

The project intentionally keeps styling lightweight.

The goal is a clean, readable file browser rather than a framework-heavy UI.

The frontend uses:

```text
HTML
CSS
vanilla JavaScript
```

File rows are arranged into simple columns for:

```text
Name
Type
Size
Modified
Action
```

No UI framework is required.

---

# Testing

A regression script is included:

```bash
./test_all.sh
```

The current automated regression pass covers:

- Project build.
- Root browse.
- Nested browse.
- Empty directories.
- Exact file sizes.
- Recursive folder sizes.
- Recursive search.
- Case-insensitive search.
- Search scope.
- Search folder-size accumulation.
- Empty/no-result searches.
- Downloads.
- Missing downloads.
- Uploads.
- Duplicate uploads.
- Empty uploads.
- Missing upload directories.
- Filename sanitization.
- 404 responses.
- 403 traversal protection.
- Similar-prefix path attacks.
- Paths containing spaces.
- Long filenames at the API level.
- Symlink/reparse-point enumeration behavior.
- Static frontend files.
- Frontend cancellation wiring.
- Deep-link/history wiring.
- SPA route serving.

Latest run:

```text
Passed: 55
Failed: 0
```

Manual browser checks are still useful for behavior that depends on a real browser:

- Visual layout.
- Browser Back/Forward.
- Breadcrumb interaction.
- Deep-link restoration after refresh.
- Search cancellation behavior.
- Browser upload/download.
- Long-filename layout.
- Singular/plural labels.

---

# Known Limitations

## Directory size performance

Recursively calculating folder sizes can become expensive for extremely large directory trees.

Possible future solutions:

- Lazy calculation.
- Background workers.
- Caching.
- Filesystem change notifications.

## Filename search only

Search currently matches file and directory names.

It does not search inside file contents.

## Symbolic-link hardening

Lexical path traversal is restricted and normal enumeration skips reparse points.

A production implementation could additionally resolve final filesystem targets before allowing direct access through known symbolic-link paths.

## Large result sets

Browse and search currently return complete result sets.

For very large directories, pagination or UI virtualization would improve scalability.

---

# Possible Future Improvements

The project intentionally focuses on a small working proof of concept.

Possible additions include:

- Delete file/folder.
- Move.
- Copy.
- Rename.
- Lazy folder sizes.
- Folder-size cache.
- Pagination.
- Virtualized rendering.
- Content search.
- Automated browser testing with Playwright.
- More granular filesystem error reporting.

---

# Final Architecture

```text
Browser
│
├── index.html
├── styles.css
└── app.js
     │
     ├── Browse
     ├── Search
     ├── Breadcrumbs
     ├── URL state
     ├── Upload
     ├── Download
     └── AbortController
          │
          ▼
FileController
     │
     ├── HTTP request validation
     ├── status-code mapping
     └── request CancellationToken
          │
          ▼
FileService
     │
     ├── ResolvePath
     ├── BrowseDirectory
     ├── SearchDirectory
     ├── directory-size accumulation
     ├── OpenDownload
     ├── UploadFile
     ├── relative client paths
     └── enumeration policy
          │
          ▼
System.IO
          │
          ▼
Configured Home Directory
```

The key design principle is:

> The browser works only with relative client paths and JSON data. All real filesystem access and security enforcement remain on the server.
