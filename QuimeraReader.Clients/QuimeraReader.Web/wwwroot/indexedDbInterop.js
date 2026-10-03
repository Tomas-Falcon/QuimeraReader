const DB_NAME = 'QuimeraReaderDB';
const DB_VERSION = 1;

function getDb() {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(DB_NAME, DB_VERSION);
        request.onupgradeneeded = (event) => {
            const db = event.target.result;
            if (!db.objectStoreNames.contains('books')) {
                db.createObjectStore('books', { keyPath: 'id' });
            }
            if (!db.objectStoreNames.contains('book_files')) {
                db.createObjectStore('book_files', { keyPath: 'id' });
            }
            if (!db.objectStoreNames.contains('sync_queue')) {
                db.createObjectStore('sync_queue', { keyPath: 'id', autoIncrement: true });
            }
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

window.quimeraIndexedDb = {
    saveBook: async function(bookJson) {
        const db = await getDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('books', 'readwrite');
            const store = tx.objectStore('books');
            store.put(JSON.parse(bookJson));
            tx.oncomplete = () => resolve(true);
            tx.onerror = () => reject(tx.error);
        });
    },
    
    getBook: async function(bookId) {
        const db = await getDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('books', 'readonly');
            const store = tx.objectStore('books');
            const request = store.get(bookId);
            request.onsuccess = () => resolve(request.result ? JSON.stringify(request.result) : null);
            request.onerror = () => reject(tx.error);
        });
    },

    getAllBooks: async function() {
        const db = await getDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('books', 'readonly');
            const store = tx.objectStore('books');
            const request = store.getAll();
            request.onsuccess = () => resolve(JSON.stringify(request.result || []));
            request.onerror = () => reject(tx.error);
        });
    },

    deleteBook: async function(bookId) {
        const db = await getDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(['books', 'book_files'], 'readwrite');
            tx.objectStore('books').delete(bookId);
            tx.objectStore('book_files').delete(bookId);
            tx.oncomplete = () => resolve(true);
            tx.onerror = () => reject(tx.error);
        });
    },

        downloadFileToDb: async function(bookId, fileType, url) {
        if (!url || url.startsWith('blob:')) return url;
        try {
            const response = await fetch(url);
            if (!response.ok) return url; // Fallback to original url if failed
            const blob = await response.blob();
            
            const db = await getDb();
            return new Promise((resolve, reject) => {
                const tx = db.transaction('book_files', 'readwrite');
                const store = tx.objectStore('book_files');
                const getReq = store.get(bookId);
                
                getReq.onsuccess = () => {
                    const data = getReq.result || { id: bookId };
                    data[fileType] = blob;
                    
                    const putReq = store.put(data);
                    putReq.onsuccess = () => resolve('blob:' + bookId + ':' + fileType);
                    putReq.onerror = () => reject(putReq.error);
                };
                getReq.onerror = () => reject(getReq.error);
            });
        } catch (e) {
            console.error('Error downloading to indexeddb', e);
            return url;
        }
    },
    saveFile: async function(bookId, fileType, arrayBuffer, mimeType) {
        const db = await getDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('book_files', 'readwrite');
            const store = tx.objectStore('book_files');
            const getReq = store.get(bookId);
            
            getReq.onsuccess = () => {
                const data = getReq.result || { id: bookId };
                data[fileType] = new Blob([arrayBuffer], { type: mimeType });
                
                const putReq = store.put(data);
                putReq.onsuccess = () => resolve(true);
                putReq.onerror = () => reject(putReq.error);
            };
            getReq.onerror = () => reject(getReq.error);
        });
    },

    getFileUrl: async function(bookId, fileType) {
        const db = await getDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('book_files', 'readonly');
            const store = tx.objectStore('book_files');
            const request = store.get(bookId);
            
            request.onsuccess = () => {
                if (request.result && request.result[fileType]) {
                    resolve(URL.createObjectURL(request.result[fileType]));
                } else {
                    resolve(null);
                }
            };
            request.onerror = () => reject(tx.error);
        });
    },
    
    // Sync Queue logic
    enqueueSync: async function(actionJson) {
        const db = await getDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('sync_queue', 'readwrite');
            const store = tx.objectStore('sync_queue');
            store.add(JSON.parse(actionJson));
            tx.oncomplete = () => resolve(true);
            tx.onerror = () => reject(tx.error);
        });
    },
    
    getSyncQueue: async function() {
        const db = await getDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('sync_queue', 'readonly');
            const store = tx.objectStore('sync_queue');
            const request = store.getAll();
            request.onsuccess = () => resolve(JSON.stringify(request.result || []));
            request.onerror = () => reject(tx.error);
        });
    },
    
    dequeueSync: async function(id) {
        const db = await getDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('sync_queue', 'readwrite');
            const store = tx.objectStore('sync_queue');
            store.delete(id);
            tx.oncomplete = () => resolve(true);
            tx.onerror = () => reject(tx.error);
        });
    }
};

window.quimeraNetwork = {
    isOnline: function() {
        return navigator.onLine;
    },
    registerListener: function(dotNetRef) {
        window.addEventListener('online', () => dotNetRef.invokeMethodAsync('OnNetworkStateChangedJS', true));
        window.addEventListener('offline', () => dotNetRef.invokeMethodAsync('OnNetworkStateChangedJS', false));
    }
};
