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










