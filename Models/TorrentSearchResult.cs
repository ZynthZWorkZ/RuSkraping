using System.ComponentModel;

namespace RuSkraping.Models;

/// <summary>
/// Represents a torrent search result with all available information
/// Implements INotifyPropertyChanged for proper UI binding
/// </summary>
public class TorrentSearchResult : INotifyPropertyChanged
{
    private string _topicId = string.Empty;
    private string _title = string.Empty;
    private string _link = string.Empty;
    private string _size = string.Empty;
    private string _seeds = "0";
    private string _leeches = "0";
    private string _author = string.Empty;
    private string _date = string.Empty;
    private string _downloadUrl = string.Empty;
    private string _imageUrl = string.Empty;
    private int _page;

    public string TopicId
    {
        get => _topicId;
        set
        {
            if (_topicId != value)
            {
                _topicId = value;
                OnPropertyChanged(nameof(TopicId));
            }
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public string Link
    {
        get => _link;
        set
        {
            if (_link != value)
            {
                _link = value;
                OnPropertyChanged(nameof(Link));
            }
        }
    }

    public string Size
    {
        get => _size;
        set
        {
            if (_size != value)
            {
                _size = value;
                OnPropertyChanged(nameof(Size));
            }
        }
    }

    public string Seeds
    {
        get => _seeds;
        set
        {
            if (_seeds != value)
            {
                _seeds = value;
                OnPropertyChanged(nameof(Seeds));
            }
        }
    }

    public string Leeches
    {
        get => _leeches;
        set
        {
            if (_leeches != value)
            {
                _leeches = value;
                OnPropertyChanged(nameof(Leeches));
            }
        }
    }

    public string Author
    {
        get => _author;
        set
        {
            if (_author != value)
            {
                _author = value;
                OnPropertyChanged(nameof(Author));
            }
        }
    }

    public string Date
    {
        get => _date;
        set
        {
            if (_date != value)
            {
                _date = value;
                OnPropertyChanged(nameof(Date));
            }
        }
    }

    public string DownloadUrl
    {
        get => _downloadUrl;
        set
        {
            if (_downloadUrl != value)
            {
                _downloadUrl = value;
                OnPropertyChanged(nameof(DownloadUrl));
            }
        }
    }

    public string ImageUrl
    {
        get => _imageUrl;
        set
        {
            if (_imageUrl != value)
            {
                _imageUrl = value;
                OnPropertyChanged(nameof(ImageUrl));
            }
        }
    }

    public int Page
    {
        get => _page;
        set
        {
            if (_page != value)
            {
                _page = value;
                OnPropertyChanged(nameof(Page));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

