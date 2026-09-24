import sys

path_worker = 'QuimeraReader.Clients/QuimeraReader.Mobile/Services/MauiOfflineSyncWorker.cs'
with open(path_worker, 'r', encoding='utf-8') as f:
    content = f.read()

# Replace file paths with URLs and save local paths
content = content.replace('book.EpubFilePath = await DownloadFileAsync(book.EpubFilePath, $\"epub_{book.Id}.epub\");', 
'''var localEpub = await DownloadFileAsync(book.EpubUrl, $"epub_{book.Id}.epub");
                    if (localEpub != book.EpubUrl) book.LocalEpubPath = localEpub;''')

content = content.replace('book.CoverImagePath = await DownloadFileAsync(book.CoverImagePath, $\"cover_{book.Id}.jpg\");', 
'''var localCover = await DownloadFileAsync(book.CoverUrl, $"cover_{book.Id}.jpg");
                    if (localCover != book.CoverUrl) book.LocalCoverPath = localCover;''')

content = content.replace('''                    if (book.AudioTracks != null)
                    {
                        foreach (var track in book.AudioTracks)
                        {
                            if (!string.IsNullOrEmpty(track.FilePath))
                            {
                                track.FilePath = await DownloadFileAsync(track.FilePath, $"audio_{book.Id}_{track.TrackNumber}.mp3");
                            }
                        }
                    }''', '''                    var localAudio = await DownloadFileAsync(book.AudioUrl, $"audio_{book.Id}.mp3");
                    if (localAudio != book.AudioUrl) book.LocalAudioPath = localAudio;''')

content = content.replace('''            string downloadUrl = serverPath;
            if (serverPath.Contains("api/Media")) {
                downloadUrl = serverPath;
            } else if (!serverPath.StartsWith("http")) {
                downloadUrl = $"api/Media/stream?filePath={Uri.EscapeDataString(serverPath)}";
            }''', '''            string downloadUrl = serverPath;''')

with open(path_worker, 'w', encoding='utf-8') as f:
    f.write(content)