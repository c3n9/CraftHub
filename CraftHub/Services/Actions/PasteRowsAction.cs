using CraftHub.Core;
using CraftHub.Domain.Models;
using CraftHub.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CraftHub.Services.Actions
{
    public class PasteRowsAction : IUndoableAction
    {
        private readonly ObservableCollection<DynamicDataRow> _rows;
        private readonly List<DynamicDataRow> _pasted;

        public PasteRowsAction(ObservableCollection<DynamicDataRow> rows,
            IEnumerable<DynamicDataRow> pasted)
        {
            _rows = rows;
            _pasted = pasted.ToList();
        }

        public string Description => Localizer.Get("UndoDescPasteRows", _pasted.Count);

        public void Undo()
        {
            foreach (var row in _pasted)
                _rows.Remove(row);
        }

        public void Redo()
        {
            foreach (var row in _pasted)
                _rows.Add(row);
        }
    }
}
