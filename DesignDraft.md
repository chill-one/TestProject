<h1>Design Choice</h1>



**FileItem**
-
- name : The name of the file. (String)
- path : The **relative path** of the current file. (String)
- type : Either file or folder. (Enum)
- size : How big the file or folder is. (long?)
- modifiedDate : when was this last modified. (DateTimeOffset)

**Reasonings**
-

- String for *name* and *path* is sound.
    - We store **relative path** as opposed to **absolute path**
    reason being that we should keep the server's real filesystem details hidden and it also works nicely with the conifurable home directroy requirement.
    - Example : `/Users/Someone/Desktop/Company-project/files/Doc/report.pdf` -> `Doc/report.pdf`
- Enum for *type*, because we want a controlled set like File and Directory.
    - Its more controlled to use **enum** instead of **Strings** like `file` or `folder` or `File` everywhere.

- A `int` isnt large enough to hold the potential size of a directory or file so we use 64-bit `long`.
    - Instead of using 0 for a default size for directory, I will be using null instead
    - `0` -> actual size is zero bytes
`null`  -> size was not calculated / does not apply

- Tells the user when the item was most recently change, which is something the user might want to know as someone else might have worked on the file.
    - Choose `DateTimeOffset` as APIs may move data between systems that may not share the same timezone.


Getting file or folder Size
-

For a file getting its size is simple.

What about directory?
**We can just recurse the directory and the directory inside right ?**

**For small system this is fine!**
However, for a folder that has thousands of inner folders and millions of files this becomes very slow.


Option A
- Every time we browse a folder, recursively calculate the full size of every subfolder.
- **Very Informative, but potentially slow**

Option B
- Only Show size for files
- **Much Faster but if user wants to know the size of a folder they can't**

Option C
- Show file sizes immediately, but calculate folder size only when specifically requested or needed.
- **User gets to decide**

Option D (Maybe for later but faster)
- Maintaing cache folder-size metadata
- More complicated writes and not enought info and may introduce another problem called **cache invalidation.**( This happens when another user uses another system to change the data in the server)



**I will choose Option C as the User gets to decide.**


 What .NET filesystem library gives acess to the operations i need 
-
**System.IO**

- Directory -> work with folders
- File -> work with files
- DirectoryInfo -> metadata about a directory 
    - **Could this have the size of the Directory?**
        - Turns out no!
- FileInfo -> metadata about a file

`Directory.GetDirectories(path)` and `Directory.GetFiles(path)`
would let us list the contents of a folder.

`FileInfo` can give us things like:
- Name
- FullName
- Length
- LastWriteTimeUtc

`DirectoryInfo` can give use folder metadata like:
- Name
- FullName
- LastWriteTimeUtc


Path Security problem 
-

Suppose our configured home directory is "/Users/Name/TestFiles"
and the user sends this path: "../../../../etc" -> this could leak other info which is BAD!

**Every path coming from the browser should be treated as untrusted input and restricted to the configured home directory.**

How i will defend against it.
- Take configured home directory
- Combine it with the user's relative path
    - use `Path.Combine(homeDir, relativePath)`
- Normalize it into an absoulte path
    - use `Path.GetFullPath(combinedPath)`
- Verify that result is still inside the home directory
    - we can't just use normal prefix to check 
        Suppose:

                Allowed:
                /Users/Name/TestFiles

                if an attacker somehow resolves to:
                [/Users/Name/TestFiles]Secret
                    same this would not work with naive prefix check
    
    - if we normialize the root as `/Users/Name/TestFiles/` instead of `/Users/Name/TestFiles`

                /Users/Name/TestFilesS ecret/report.pdf. 
                /Users/Name/TestFiles/ (They are nolonger a prefix.)

        - Since this project needs to build with standard .NET tooling and could run on either Window or macOS/Linux we need to follow seperate Conventions respectively.

            - First we could use `Path.DirectorySeparatorChar` which allows us to find the respective Seperator. After, use `EndsWith` on the normalized path before appending anything since its possible that we can have '/' at the end.

- Reject if otherwise

Example:

    Home:
        /Users/Name/TestFiles

    Input:
        Documents/../Projects

    Normalized:
        /Users/Name/TestFiles/Projects (this is fine!)


    Input:
    ../../etc

    Normalized:
        /Users/etc   ← outside our allowed root


Edge Case
-

What if the frontend wants the root folder itself:

`relativePath = ""`

After normalization, the result might be:

`/Users/Name/TestFiles/`

**This should obviously be allowed too**

Validation needs to support:
- Root itself : /Users/Name/TestFiles/ (good)
- Anything underneath root: /Users/Name/TestFiles/Documents/report.pdf (good)
- Anything outside root: /Users/Name/etc/passwd (BAD)

This will be done in `ResolvePath()` inside FileService.


Browsing
-
In System.IO we can choose between two approaches:

**For Files**

`Directory.GetFiles(path)`
Mainly gives you file paths and for every path, I'd probably need to create a FileInfo afterward to get Name, Length, LastWriteTimeUtc.

`DirectoryInfo(path).GetFiles()`
You immediately get an array of FileInfo objects. Where each object already has the info we need.

**Full Name** This should not be sent directly to the front end as it contains the absolute path to the current file.

To Resolve this we can use `Path.GetRelativePath()` to turn the given absolute path into relative path.

**For Directory**

Similer to file system I can use `directory.GetDirectories()` which returns `DirectoryInfo` objects and use `Path.GetRelativePath()` to turn the given absolute path into relative path.

**Error Handling**
-

Currently when the backend service throws an error, I don't have an actual HTTP responses which is meaningful.

Example: 

    DirectoryNotFoundException
            -> 404 Not Found

    UnauthorizedAccessException
            -> 403 Forbidden

I can use the `NotFound()` helper method within the controller and return an HTTP
404 **Not Found** status code back to the client for `DirectoryNotFoundException`

Similary I can use `StatusCode(StatusCode.Status403Forbidden)` for the `UnauthorizedAcessException`.

**Connect frontend to the API**
-

JavaScript should render the UI, while the **server** returns data than server-rendered HTML as specified in the spec.

Right now the file **Program.cs** has 

`app.UseStaticFiles()`
- This middleware allows the browser access files inside wwroot.
- Without `app.UseStaticFiles()`, having files inside wwroot be itself doesn't amke them availabel to the browser.

We can also add.

`app.UseDefaultFiles()`
- This middleware tells ASP.NET when someone requests a direcotory such as `GET /`, look for a default file such as **index.html**.
- **Does not actually send index.html** to the browser which would contradict with the spec, It only figures out which default file should be used.

**app.js**


This is where I will create the `loadFiles()` function which will fetch from the endpoint `/api/files` using `fetch()`and convert the response into JSON.

**How will I render the file?**
1. Find #file-list 
    
    Use `document.getElementById()`
2. Clear its existing contents

3. Loop through the new data
4. Create an HTML element for each FileItem

    Use `document.createElement()`
5. Put its name/type/size on the element 

    Use `element.textContent = ...`
6. Append it to #file-list

    Use `parent.appendChild()`

The logic for this will be inside `renderItems(data);` function.

**Folder Navigation**
-
We want:

    Click Projects
        ↓
    GET /api/files?path=Projects
        ↓
    render contents of Projects


A change we can make is make `loadFiles()` accept a path.

    loadFiles("")
        → root

    loadFiles("Projects")
        → Projects folder

    loadFiles("Projects/somefile")
        → nested folder

The API URL needs to become something like:

    /api/files?path=Projects

Because paths can contain **spaces** and **special characters**. Manually concatenating the raw path is **Not an option**.
For this we can use 

`encodeURLComponent(path)` 
- Converts a string variable named path into a URL-safe format by escaping special characters.

Example:

    const searchPhrase = "cats? & dogs";

    console.log(encodeURIComponent(searchPhrase));
    // Output: cats%3F%20%26%20dogs

The browser would think `?` and `&` were code instructions for a new website route.


Making Directories clickable
-

To make a directory clickable we can create a click handler inside `renderItems()`. Only the directory should be clickable.

We can reuse `loadFiles(path)` which works naturally as the backend is already returning paths relative to the configured home directory.

**We have a problem which is once you enter a folder, you can't go back.**
Which is one of the specs where we needs to be deep-linkable and stored in the URL.

Deep-linkable
-
A "deep linkable" page, screen, or piece of content is one that can be directly opened using a specific web address or link, instead of forcing the user to start at a home page or a main menu.

    Root
    https://localhost:7146/

    Click Projects
    https://localhost:7146/?path=Projects

    Click PulseWatch
    https://localhost:7146/?path=Projects%2FPulseWatch


and 

        Refresh page
            ↓
    read path from URL
            ↓
    load same directory

In my current process clicking a directory does: `loadFiles(item.path)`  which loads the folder, however does not change the browser URL.


To turn `https://localhost:7146/` into `https://localhost:7146/?path=Projects` we can use:

`url.searchParams.set("path", path);`
- Gives JavaScript an object representing the URL.

What if the current path contains illegal characters:
- `URLSearchParams` handles it.

`history.pushState()` 
- changes the URL without refreshing the entire page.

The logic for this will be in the function `navigateTo()`.

One problem we have right now is loading the inital folder from the URL as of right now am using `loadFiles()`which ignores the URL and loads the root even if the URL had something like `https://localhost:7146/?path=Projects`.

    //Grab the url
    const params = new URLSearchParams(window.location.search);
    const initialPath = params.get("path") ?? "";

    //Use root if null else use the given url
    loadFiles(initialPath);

**We have a bug**

Project/PulseWatch

Clicking the browser **Back Button**, the URL will change back to Projects, while the page still displays PulseWatch files/folders.

Reason
- `pushState()` changes history, but we haven't told JavaScript what to do when the user navigates through the history. For this reason the browser gives us a `popstate` event to resolve this kind of issue.


Creating breadcrumb
-
Create a navigation line which tracks where the user currently is 

I will use `path.split('\')` to split at `\` of the path which will provide us with the nested folder.

From thier loop through each segments and create a button to thier respective path.

The logic for this is in the function `renderBreadcrumbs(path)`.


**Search functionality**
-
**Design choice**

if am currently inside

    Projects/

and search for 'report'

1. Search look only at the items directly inside Projects
2. Recursively search everything underneath the current directory

For this i will got with option **number 2** as searching the filesytem for the file/directory recursively would provide the use more info, if such file/directory they are looking for exist further down the root and can directly traverse to it via **Search**.

we can use `directory.GetFiles("report.pdf", SearchOption.AllDirectores)` to recursively look underneath the current directory.
However this locks the search to exact filename(report.pdf).

A better desgin would be to create `SearchDirectory(relativePath, query)` where we recursively enumerate items and decide ourselves whether 

    item.Name contains query

rather than depending entirely on a filesystem wildcard.


`directory.EnumerateDirectories("*",SearchOption.AllDirectories)`
- Returns a `DirectoryInfo` objects that are nested within the current directory recursively.


`directory.EnumerateFiles("*",SearchOption.AllDirectories)`
- Returns a `FileInfo` objects that are nested within the current directory recursively.

`file\dir.Name.Contains(query, StringComparison.OrdinalIgnoreCase)` 
- To make the search case-insensitive.

`SearchOption.AllDirectores` has a problem which is, on a real filesystem if can throw if it reaches a directory the process cannot acess.


Designing the search endpoints
-
The Controller will have another function for search called `Search()` which is very similer to `Browse()` only thing different is `Search()` calls `_fileService.SearchDirectory(path, query)` and follows the same structure for error handling.


For frontend we will have a search bar where the user can type the file/directory they are looking for under the current directory. Using the `searchForm.addEventListner()` we can create a event where following will take place.

    submit search 
        ↓
    prevent normal page refresh 
        ↓
    read search input 
        ↓
    figure out current directory
        ↓
    call /api/files/search
        ↓
    render results

Right now, if you search for 'report' inside the directory Projects the URL still looks like `/?path=Projects` what we want is `/?path=Projects&search=report` so the URL becomes the source of **Truth for both** 

    Current Folder
    Current Search

For this we will create a `navigateSearch()` similar to `navigateTo()` in our app.js.

Since search can happen from several situation:

    Search form submit
    Page refresh
    Browser Back
    Browser Forward

so its probably a good idea to extract the logic.


File Download
-
The flow would look something like this:

    User clicks Download
            ↓
    "Projects/report.pdf"
            ↓
    GET /api/files/download?path=Projects/report.pdf
            ↓
    FileController
            ↓
    FileService
            ↓
    ResolvePath()
            ↓
    Open file
            ↓
    Stream file to browser

Using a **Stream** allows us to send small pieces of data, this is very good for large size as the server only needs to hold small amount of data at a time.

The two things we need for a download is a filename and the stream containing the file data.

**Filename**
- Tells the controller what name the browser should use when downloading them.

**Stream**
- Contains the actual bytes.

pipeline:

    FileService
        ↓
    find + validate "Projects/report.pdf"
        ↓
    open FileStream
        ↓
    return:
        Stream
        FileName = "report.pdf"
        

    FileController
        ↓
    construct HTTP file response
        ↓
    Browser receives:
        bytes + filename
        ↓
    report.pdf downloads










