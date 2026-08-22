const searchForm = document.getElementById("search-form");
const searchInput = document.getElementById("search-input");

const uploadForm = document.getElementById("upload-form");
const fileInput = document.getElementById("file-input");


async function loadFiles(path = "") {
    try 
    {
        //Turns the path into a URL-safe format
        const response = await fetch(
            `/api/files?path=${encodeURIComponent(path)}`
        );

        if(!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }

        const data = await response.json();
        
        renderBreadcrumbs(path);
        renderItems(data);
    }
    catch (error)
    {
        console.error('Fetch failed:', error);
    }
}


function renderItems(data) {
    const fileList = document.getElementById("file-list");

    //Clear its existing contents
    fileList.innerHTML = '';

    //Loop through the new file/dic data
    data.forEach(item => {
        
        //create an html element for each fileitem
        const newListItem = document.createElement('li');
        

        //We dont have size when its a Directory
        if (item.type === "Directory")
        {
            newListItem.textContent = 
            `${item.name} 
            (${item.type})`;

            //Create buttons for directory
            newListItem.addEventListener("click",
                () => {
                    navigateTo(item.path);
                }
            );
        }
        else
        {
            newListItem.textContent =
            `${item.name} 
            (${item.type}) - 
            ${item.size} bytes`;

            const downloadButton = document.createElement("button");
            downloadButton.textContent = "Download";

            //if clicked on download directly go to the url which will download the file
            downloadButton.addEventListener("click", () => {
                window.location.href =
                    `/api/files/download?path=${encodeURIComponent(item.path)}`;
            });

            newListItem.appendChild(downloadButton);
        }

        fileList.appendChild(newListItem);
    });
}

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

    //Remove search param regardless of whether the destination is root
    url.searchParams.delete("search");
    searchInput.value = "";
    //Change the url without refreshing the entire page
    window.history.pushState({}, "", url);

    loadFiles(path);
}

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

    //If our path is null or empty
    if (!path)
    {
        return;
    }

    //may need to change this later
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

        //Create a new button when clicked maps to thier respective directory
        const newButton = document.createElement("button");
        newButton.textContent = segment;

        newButton.addEventListener("click", () => {
            navigateTo(breadcrumbPath);
        });
        breadcrumbs.appendChild(newButton)
    });


}

async function searchFiles(path, query) 
{

    try
    {
        // Call search API
        const response = await fetch(
            `/api/files/search?path=${encodeURIComponent(path)}&query=${encodeURIComponent(query)}`
            );

        if (!response.ok) 
        {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        // Convert response to JSON
        const data = await response.json();

        //Show the bread crumbs
        renderBreadcrumbs(path)

        // Render results
        renderItems(data);
    }
    catch (error)
    {
        console.error("Search failed:", error);
    }
    
}



const params = new URLSearchParams(window.location.search);
const initialPath = params.get("path") ?? "";
const initialSearch = params.get("search") ?? "";

if (initialSearch)
{

    //populate the input
    searchInput.value = initialSearch;
    searchFiles(initialPath, initialSearch);
}
else
{
    loadFiles(initialPath);
}

//The popsate fires when the user uses the back/forward button
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

    // Get current directory from URL
    const params = new URLSearchParams(window.location.search);
    const path = params.get("path") ?? "";

    //if the query is an empty string send them to the path
    if (!query) {
        navigateSearch(path, "");
        loadFiles(path);
        return;
    }

    //Update URL
    navigateSearch(path, query);

    //SearchFiles
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
        //Post the data to the backend
        const response = await fetch(
            `/api/files/upload?path=${encodeURIComponent(path)}`,
            {
                method: "POST",
                body: formData
            }
        );

        if(!response.ok) {
            throw new Error(`Http error! status: ${response.status}`);
        }

        //empty the input
        fileInput.value = "";
        navigateTo(path);
    }
    catch (error)
    {
        console.error("Upload failed:", error);
    }
});
