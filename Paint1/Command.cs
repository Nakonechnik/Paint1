using System.Collections.Generic;

namespace VectorEditor
{
    public interface ICommand
    {
        void Undo();
    }

    public class AddShapeCommand : ICommand
    {
        private List<ShapeData> shapes;
        private ShapeData data;

        public AddShapeCommand(List<ShapeData> s, ShapeData d)
        {
            shapes = s;
            data = d;
        }

        public void Undo()
        {
            shapes.Remove(data);
        }
    }
}
