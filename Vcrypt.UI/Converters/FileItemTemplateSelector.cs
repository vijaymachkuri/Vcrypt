using System.Windows;
using System.Windows.Controls;
using Vcrypt.Core.Models;

namespace Vcrypt.UI.Converters
{
    public class FileItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? FolderTemplate { get; set; }
        public DataTemplate? FileTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is EncryptedItemModel model)
            {
                if (model.IsFolder) return FolderTemplate;
                return FileTemplate;
            }
            return base.SelectTemplate(item, container);
        }
    }
}
