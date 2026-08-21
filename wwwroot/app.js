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
loadFiles();

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
                    loadFiles(item.path);
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