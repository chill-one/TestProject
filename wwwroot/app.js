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
        
        renderItems(data)
    }
    catch (error)
    {
        console.error('Fetch failed:', error);
    }
}
const params = new URLSearchParams(window.location.search);
const initialPath = params.get("path") ?? "";

loadFiles(initialPath);

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
                    naviageTo(item.path);
                }
            )
        }
        else
        {
            newListItem.textContent =
            `${item.name} 
            (${item.type}) - 
            ${item.size} bytes`;
        }

        fileList.appendChild(newListItem);
    });
}

function naviageTo(path)
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

    //Change the url without refreshing the entire page
    window.history.pushState({}, "", url);

    loadFiles(path)
}