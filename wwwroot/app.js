async function loadFiles() {
    try 
    {
        const response = await fetch("/api/files");

        if(!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }

        const data = await response.json();
        
        console.log(data);
    }
    catch (error)
    {
        console.error('Fetch failed:', error);
    }
}

loadFiles();