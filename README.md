# File Browser

A lightweight single-page file browser built with ASP.NET Core and vanilla JavaScript.

The application exposes a configurable server-side home directory and lets users browse, recursively search, upload, and download files without exposing absolute server filesystem paths.

## Features

- Browse files and directories.
- Recursive, case-insensitive filename search.
- File and recursive folder sizes.
- File/folder counts for the current view.
- Last-modified date and time.
- File upload.
- Streamed file download.
- Breadcrumb navigation.
- Deep-linkable directory and search state.
- Browser Back/Forward support.
- Request cancellation for obsolete searches.
- Configurable filesystem root.
- Path-traversal protection.
- Cross-platform client paths.

## Tech Stack

- .NET 8
- ASP.NET Core Web API
- C#
- `System.IO`
- Vanilla JavaScript
- HTML
- CSS

## Project Structure

```text
Controllers/
    FileController.cs

Models/
    FileItem.cs
    FileItemType.cs

Services/
    FileService.cs

wwwroot/
    index.html
    app.js
    styles.css

appsettings.json
Program.cs
DesignDocs.md
test_all.sh
```

## Configuration

Configure the home directory in `appsettings.json`.

Example:

```json
{
  "FileBrowser": {
    "HomeDirectory": "/Users/Someone/TestFiles"
  }
}
```

The application validates this directory at startup.

## Run

From the project directory:

```bash
dotnet run
```

Open the localhost URL printed by ASP.NET.

## API

### Browse

```http
GET /api/files?path=<relative-directory-path>
```

Returns the direct files and directories inside the requested directory.

Example:

```http
GET /api/files?path=Projects
```

### Search

```http
GET /api/files/search?path=<relative-directory-path>&query=<query>
```

Recursively searches below the supplied directory using case-insensitive name matching.

Example:

```http
GET /api/files/search?path=Projects&query=report
```

### Download

```http
GET /api/files/download?path=<relative-file-path>
```

Streams the requested file to the browser.

### Upload

```http
POST /api/files/upload?path=<relative-directory-path>
```

The request body uses:

```text
multipart/form-data
```

with the file under the key:

```text
file
```

Duplicate filenames are rejected rather than overwritten.

## Architecture

```text
Browser
    ↓
FileController
    ↓
FileService
    ↓
System.IO
    ↓
Configured Home Directory
```

The browser never receives absolute server filesystem paths.

All client paths are relative to the configured home directory.

## Design Decisions

### Filesystem Boundary

Every path received from the browser is treated as untrusted.

The service:

1. Combines the relative path with the configured home directory.
2. Normalizes it using `Path.GetFullPath()`.
3. Confirms that the normalized result remains under the configured root.
4. Rejects the request otherwise.

This prevents ordinary `../` path traversal outside the allowed filesystem root.

### Cross-Platform Client Paths

The backend converts filesystem paths into relative paths and normalizes separators to `/`.

This allows the frontend to use the same path representation whether the server runs on Windows, macOS, or Linux.

### Folder Sizes

Files use `FileInfo.Length`.

Directory sizes are calculated recursively from contained files.

During search, matching directory sizes are not calculated with a second recursive scan. Instead, file sizes are accumulated into ancestor directory totals during the same search traversal.

### Search Performance

Search uses one recursive:

```csharp
EnumerateFileSystemInfos(...)
```

pass.

This avoids separate recursive traversals for files and directories.

The search also skips reparse points and ignores inaccessible entries.

### Cancellation

Recursive filesystem work accepts ASP.NET's request `CancellationToken`.

The frontend uses `AbortController`.

When a newer search starts or the user navigates elsewhere, the old search request is aborted and the backend can stop its filesystem work.

### Upload Safety

Uploaded filenames are sanitized with:

```csharp
Path.GetFileName(...)
```

Files are created with:

```csharp
FileMode.CreateNew
```

which atomically rejects duplicate filenames rather than silently overwriting an existing file.

### Downloads

Downloads return a `FileStream`.

This allows the server to stream file contents instead of loading the entire file into memory before sending it.

### URL State

The browser URL stores both directory and search state.

Examples:

```text
/?path=Projects
/?path=Projects%2FAPI
/?path=Projects&search=report
```

This allows refresh, direct links, and browser Back/Forward to restore the same state.

## Testing

Run the automated regression suite:

```bash
chmod +x test_all.sh
./test_all.sh
```

Latest result:

```text
Passed: 55
Failed: 0
```

The automated checks cover:

- Build.
- Browse.
- Nested browse.
- Exact file/folder sizes.
- Recursive search.
- Case-insensitive search.
- Search scope.
- Upload/download.
- Duplicate uploads.
- Error responses.
- Path traversal.
- Filename sanitization.
- Paths with spaces.
- Symlink/reparse-point enumeration.
- Frontend file presence and wiring.
- Deep-link route serving.
- Search cancellation wiring.

A short manual browser pass is also recommended for:

- Back/Forward navigation.
- Breadcrumb interaction.
- Deep-link restoration after refresh.
- Visible date/time formatting.
- Search cancellation behavior.
- Upload/download from the browser.
- Long filename layout.

## Known Limitations

- Recursive folder-size calculation can become expensive on extremely large directory trees.
- Search matches filenames and directory names, not file contents.
- Browse/search currently return complete result sets rather than paginated results.
- The current sandbox protects against lexical path traversal and skips reparse points during normal enumeration. A production implementation could additionally resolve filesystem-link targets before permitting direct access through a known link path.

## Future Improvements

- Lazy/background folder-size calculation.
- Folder-size caching.
- Pagination or virtualized rendering.
- Delete, move, copy, and rename.
- File-content search.
- Automated browser testing with Playwright.

## Additional Design Notes

For the full reasoning behind the architecture, alternatives considered, security decisions, search optimization, folder-size strategy, and URL-state design, see:

```text
DesignDocs.md
```
