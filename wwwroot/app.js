const searchForm = document.getElementById("search-form");
const searchInput = document.getElementById("search-input");

const uploadForm = document.getElementById("upload-form");
const fileInput = document.getElementById("file-input");

const statusElement = document.getElementById("status");
let activeSearchController = null;


/** Stops the search currently running, if there is one. */
function cancelActiveSearch()
{
    // Abort the current active search.
    if (activeSearchController)
    {
        activeSearchController.abort();
        activeSearchController = null;
    }
}


/** Loads a directory from the API and refreshes the file browser. */
async function loadFiles(path = "") {
    try 
    {
        // Turn the path into a URL-safe format.
        const response = await fetch(
            `/api/files?path=${encodeURIComponent(path)}`
        );

        if(!response.ok) {
            const errorData = await response.json();

            throw new Error(
                errorData.error ?? `HTTP error! status: ${response.status}`
            );
        }

        const data = await response.json();
        
        renderBreadcrumbs(path);
        renderSummary(data);
        renderItems(data);

        clearStatus();
    }
    catch (error)
    {   
        showStatus(error.message);
        console.error('Fetch failed:', error);
    }
}


/** Renders files and folders, including their available actions. */
function renderItems(data, showPath = false) {
    const fileList = document.getElementById("file-list");

    // Clear its existing contents.
    fileList.innerHTML = '';

    // Loop through the new file and directory data.
    data.forEach(item => {
        
        // Create an HTML element for each file item.
        const newListItem = document.createElement('li');
        

        // Directories do not include a size in this view.
        if (item.type === "Directory")
        {
            // Show the full path in search results for extra context.
            const displayName = showPath ? item.path : item.name;
            newListItem.textContent = 
            `${displayName} (${item.type}) - ${formatBytes(item.size)} - Modified: ${formatDate(item.lastModifiedDate)}`;

            // Create a button for the directory.
            newListItem.addEventListener("click",
                () => {
                    navigateTo(item.path);
                }
            );
        }
        else
        {
            const displayName = showPath ? item.path : item.name;
            newListItem.textContent =
            `${displayName} (${item.type}) - ${formatBytes(item.size)} - Modified: ${formatDate(item.lastModifiedDate)}`;

            const downloadButton = document.createElement("button");
            downloadButton.textContent = "Download";

            // Download the file when the button is clicked.
            downloadButton.addEventListener("click", () => {
                window.location.href =
                    `/api/files/download?path=${encodeURIComponent(item.path)}`;
            });

            newListItem.appendChild(downloadButton);
        }

        fileList.appendChild(newListItem);
    });
}

/** Updates the URL to reflect an active search without reloading the page. */
function navigateSearch(path, query)
{
    const url = new URL(window.location);

    if (path)
    {
        url.searchParams.set("path", path);
    }
    else
    {
        url.searchParams.delete("path");
    }

    if (query)
    {
        url.searchParams.set("search", query);
    }
    else
    {
        url.searchParams.delete("search");
    }

    window.history.pushState({}, "", url);
}

/** Moves to a directory and clears any active search. */
function navigateTo(path)
{
    const url = new URL(window.location);

    if (path)
    {
        url.searchParams.set("path", path);
        
    }
    else
    {
        url.searchParams.delete("path");
    }

    // Remove the search parameter when navigating.
    url.searchParams.delete("search");
    searchInput.value = "";
    // Change the URL without refreshing the entire page.
    window.history.pushState({}, "", url);

    loadFiles(path);
}

/** Builds clickable breadcrumb buttons for the current directory. */
function renderBreadcrumbs(path)
{
    const breadcrumbs = document.getElementById("breadcrumbs");

    breadcrumbs.innerHTML = "";

    const home = document.createElement("button");

    home.textContent = "Home";

    home.addEventListener("click", () => {
        navigateTo("");
    });

    breadcrumbs.appendChild(home);

    // If the path is null or empty, only show Home.
    if (!path)
    {
        return;
    }

    // Build one breadcrumb for each path segment.
    const segments = path.split("/");
    let currentPath = "";

    segments.forEach(segment => {
        if (!currentPath)
        {
            currentPath = segment;
        }
        else
        {
            currentPath = currentPath + "/" + segment;
        }
        const breadcrumbPath = currentPath;

        // Create a button that opens the corresponding directory.
        const newButton = document.createElement("button");
        newButton.textContent = segment;

        newButton.addEventListener("click", () => {
            navigateTo(breadcrumbPath);
        });
        breadcrumbs.appendChild(newButton)
    });


}

/** Shows the folder count, file count, and total size for a result set. */
function renderSummary(items, includeDirectorySizes = true)
{
    let fileCount = 0;
    let directoryCount = 0;
    let totalSize = 0;

    items.forEach(item => {
        
        if (item.type === "Directory")
        {
            directoryCount++;
            if(includeDirectorySizes)
            {
                totalSize += item.size ?? 0;
            }
        }
        else
        {
            fileCount++;
            totalSize += item.size ?? 0;
        }

    });

    // Handle singular and plural labels.
    const folderLabel = directoryCount === 1 ? "folder" : "folders";
    const fileLabel = fileCount === 1 ? "file" : "files";

    const summary = document.getElementById("summary");

    const sizeLabel = includeDirectorySizes ? "Total size" : "Matched file size";
    summary.textContent =`${directoryCount} ${folderLabel} • ${fileCount} ${fileLabel} • ${sizeLabel} ${formatBytes(totalSize)}`;
}


/** Converts a byte count into a short, readable size. */
function formatBytes(bytes)
{
    if (bytes === 0)
    {
        return "0 bytes";
    }

    const units = ["bytes", "KB", "MB", "GB", "TB"];
    let unitIndex = 0;
    let size = bytes;

    while (size >= 1024 && unitIndex < units.length - 1)
    {
        size /= 1024;
        unitIndex++;
    }

    return `${size.toFixed(1)} ${units[unitIndex]}`;
}


/** Searches the API and renders the matching files and folders. */
async function searchFiles(path, query) 
{
    cancelActiveSearch();

    const controller = new AbortController();
    activeSearchController = controller;

    showStatus("Searching...");

    try
    {
        // Call the search API.
        const response = await fetch(
            `/api/files/search?path=${encodeURIComponent(path)}&query=${encodeURIComponent(query)}`,
            {
                signal: controller.signal
            }
            );

        if (!response.ok) 
        {
            const errorData = await response.json();

            throw new Error(
                errorData.error ?? `HTTP error! status: ${response.status}`
            );
        }
        // Convert the response to JSON.
        const data = await response.json();

        // Show the breadcrumbs.
        renderBreadcrumbs(path)
        // Add the summary.
        renderSummary(data, false);
        // Render results with their paths.
        renderItems(data, true);

        clearStatus();
    }
    catch (error)
    {
        // Ignore errors caused by cancelling the search.
        if (error.name == "AbortError")
        {
            return;
        }
        showStatus(error.message);
        console.error("Search failed:", error);
    }
    finally
    {
        // Clean up only if this is still the active request.
        if (activeSearchController === controller)
        {
            activeSearchController = null;
        }
    }
}

/** Displays a status message below the file browser. */
function showStatus(message)
{
    statusElement.textContent = message;
}

/** Removes the current status message. */
function clearStatus()
{
    statusElement.textContent = "";
}

/** Formats an API date as a short, local calendar date. */
function formatDate(dateString)
{
    if(!dateString)
    {
        return "";
    }
    const date = new Date(dateString);

    return date.toLocaleDateString();
}



const params = new URLSearchParams(window.location.search);
const initialPath = params.get("path") ?? "";
const initialSearch = params.get("search") ?? "";

if (initialSearch)
{

    // Populate the input.
    searchInput.value = initialSearch;
    searchFiles(initialPath, initialSearch);
}
else
{
    loadFiles(initialPath);
}

// The popstate event fires when the user uses the back/forward buttons.
window.addEventListener("popstate", () => {
    const params = new URLSearchParams(window.location.search);
    const path = params.get("path") ?? "";

    const search = params.get("search") ?? "";

    if (search)
    {
        searchInput.value = search;
        searchFiles(path, search);
    }
    else
    {
        searchInput.value = "";
        loadFiles(path);
    }
});


searchForm.addEventListener("submit", (event) => {
    event.preventDefault();

    const query = searchInput.value.trim();

    // Get the current directory from the URL.
    const params = new URLSearchParams(window.location.search);
    const path = params.get("path") ?? "";

    // If the query is empty, return to the current directory.
    if (!query) {
        navigateSearch(path, "");
        loadFiles(path);
        return;
    }

    // Update the URL.
    navigateSearch(path, query);

    // Search the files.
    searchFiles(path, query);

});


uploadForm.addEventListener("submit", async (event) => {
    event.preventDefault();

    const file = fileInput.files[0];

    if(!file){
        return;
    }

    const params = new URLSearchParams(window.location.search);
    const path = params.get("path") ?? "";

    const formData = new FormData();

    formData.append("file", file);

    try
    {
        // Post the data to the backend.
        const response = await fetch(
            `/api/files/upload?path=${encodeURIComponent(path)}`,
            {
                method: "POST",
                body: formData
            }
        );

        if(!response.ok) {
            const errorData = await response.json();

            throw new Error(
                errorData.error ?? `HTTP error! status: ${response.status}`
            );
        }
        
        clearStatus();
        // Empty the input.
        fileInput.value = "";
        navigateTo(path);
    }
    catch (error)
    {
        showStatus(error.message);
        console.error("Upload failed:", error);
    }
});
