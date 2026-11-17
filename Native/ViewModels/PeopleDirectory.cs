using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Native.Bluetooth;
using Native.Models;

namespace Native.ViewModels;

/// <summary>Junta a agenda do iPhone com fotos carregadas uma vez só (para as listas não travarem).</summary>
public sealed class PeopleDirectory
{
    private readonly ContactService _contacts;
    private readonly Dictionary<string, ImageSource?> _photos = new(StringComparer.OrdinalIgnoreCase);

    public PeopleDirectory(ContactService contacts)
    {
        _contacts = contacts;
        _contacts.ContactsChanged += () => _photos.Clear();
    }

    public ContactService Service => _contacts;

    public string? NameFor(string? address) => _contacts.Find(address)?.Name;

    public ImageSource? PhotoFor(string? nameOrAddress) => Photo(_contacts.Find(nameOrAddress));

    public ImageSource? Photo(ContactRecord? contact)
    {
        var path = _contacts.PhotoPath(contact);
        if (path is null) return null;
        if (_photos.TryGetValue(path, out var cached)) return cached;

        ImageSource? image = null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 96;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            image = bmp;
        }
        catch (Exception)
        {
            // Foto corrompida: fica com as iniciais.
        }
        _photos[path] = image;
        return image;
    }

    public void Decorate(PersonItem person, string? lookup)
    {
        var contact = _contacts.Find(lookup);
        if (contact is null) return;
        if (person.Name.Length == 0 || person.Name == lookup) person.Name = contact.Name;
        person.Photo = Photo(contact);
    }

    public static bool FileExists(string? path) => path is not null && File.Exists(path);
}
