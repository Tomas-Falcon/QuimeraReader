using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using QuimeraReader.Shared.Interfaces;
using QuimeraReader.Shared.Models;

namespace QuimeraReader.Web.Services
{
    public class WebLocalBookRepository : ILocalBookRepository
    {
        private readonly IJSRuntime _jsRuntime;

        public WebLocalBookRepository(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public Task EnsureCreatedAsync() => Task.CompletedTask;

        public async Task<List<Book>> GetOfflineBooksAsync()
        {
            var json = await _jsRuntime.InvokeAsync<string>("window.quimeraIndexedDb.getAllBooks");
            return string.IsNullOrEmpty(json) ? new List<Book>() : JsonSerializer.Deserialize<List<Book>>(json) ?? new List<Book>();
        }

        public async Task<Book?> GetBookByIdAsync(int id)
        {
            var json = await _jsRuntime.InvokeAsync<string>("window.quimeraIndexedDb.getBook", id);
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<Book>(json);
        }

        public async Task SaveBookAsync(Book book)
        {
            await _jsRuntime.InvokeVoidAsync("window.quimeraIndexedDb.saveBook", JsonSerializer.Serialize(book));
        }

        public async Task SaveBooksAsync(IEnumerable<Book> books)
        {
            foreach(var book in books) {
                await SaveBookAsync(book);
            }
        }

        public async Task DeleteBookAsync(int id)
        {
            await _jsRuntime.InvokeVoidAsync("window.quimeraIndexedDb.deleteBook", id);
        }

        public async Task UpdateProgressAsync(int id, string cfi, double? audioPosition, double? percentage)
        {
            var book = await GetBookByIdAsync(id);
            if (book != null)
            {
                if (!string.IsNullOrEmpty(cfi)) book.CurrentEpubCfi = cfi;
                if (audioPosition.HasValue) book.CurrentAudioPosition = audioPosition.Value;
                if (percentage.HasValue) book.PercentageCompleted = percentage.Value;
                await SaveBookAsync(book);
            }
            
            var syncAction = new { Type = "Progress", BookId = id, Cfi = cfi, AudioPosition = audioPosition, Percentage = percentage };
            await _jsRuntime.InvokeVoidAsync("window.quimeraIndexedDb.enqueueSync", JsonSerializer.Serialize(syncAction));
        }

        public async Task QueueAnnotationAsync(int bookId, string cfiRange, string selectedText, string colorHex, string note)
        {
            var syncAction = new AnnotationQueueItem { 
                Id = Guid.NewGuid().ToString(),
                BookId = bookId,
                CfiRange = cfiRange, 
                SelectedText = selectedText, 
                ColorHex = colorHex, 
                Note = note 
            };
            await _jsRuntime.InvokeVoidAsync("window.quimeraIndexedDb.enqueueSync", JsonSerializer.Serialize(syncAction));
            
            // Fetch current annotations for book and append
            var anns = await GetAnnotationsAsync(bookId);
            anns.Add(new AnnotationDto {
                
                CfiRange = cfiRange,
                SelectedText = selectedText,
                ColorHex = colorHex,
                Note = note,
                CreatedAt = DateTime.UtcNow
            });
            await SyncAnnotationsAsync(bookId, anns);
        }

        public async Task<List<AnnotationQueueItem>> GetQueuedAnnotationsAsync()
        {
            var json = await _jsRuntime.InvokeAsync<string>("window.quimeraIndexedDb.getSyncQueue");
            if (string.IsNullOrEmpty(json)) return new List<AnnotationQueueItem>();
            
            var items = new List<AnnotationQueueItem>();
            using (var doc = JsonDocument.Parse(json))
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.TryGetProperty("Type", out var typeProp) && typeProp.GetString() == "Progress")
                        continue;
                        
                    items.Add(JsonSerializer.Deserialize<AnnotationQueueItem>(el.GetRawText())!);
                }
            }
            return items;
        }

        public async Task RemoveQueuedAnnotationAsync(string id)
        {
            await _jsRuntime.InvokeVoidAsync("window.quimeraIndexedDb.dequeueSync", id); 
        }

        public async Task<List<AnnotationDto>> GetAnnotationsAsync(int bookId)
        {
            var json = await _jsRuntime.InvokeAsync<string>("window.localStorage.getItem", $"annotations_{bookId}");
            return string.IsNullOrEmpty(json) ? new List<AnnotationDto>() : JsonSerializer.Deserialize<List<AnnotationDto>>(json) ?? new List<AnnotationDto>();
        }

        public async Task SyncAnnotationsAsync(int bookId, List<AnnotationDto> annotations)
        {
            await _jsRuntime.InvokeVoidAsync("window.localStorage.setItem", $"annotations_{bookId}", JsonSerializer.Serialize(annotations));
        }

        public async Task<string?> GetBookFileUrlAsync(int bookId, string fileType)
        {
            return await _jsRuntime.InvokeAsync<string>("window.quimeraIndexedDb.getFileUrl", bookId, fileType);
        }
    }
}
