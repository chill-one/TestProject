<h1>Design Choice</h1>

<h3> Information needed for each file </h3>

**FileItem**
- name : The name of the file. (String)
- path : The **relative path** of the current file. (String)
- type : Either file or folder. (Enum)
- size : How big the file or folder is. (long?)
- modifiedDate : when was this last modified. (DateTimeOffset)

**Reasonings**

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


<h3> Getting file or folder Size </h3>

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




