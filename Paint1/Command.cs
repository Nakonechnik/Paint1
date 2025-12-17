using System.Collections.Generic;
using System.Windows.Shapes;

namespace Paint1
{
    public interface ICommand
    {
        void Execute();
        void Undo();
    }

    public class AddShapeCommand : ICommand
    {
        private System.Windows.Controls.Canvas canvas;
        private Shape shape;

        public AddShapeCommand(System.Windows.Controls.Canvas canvas, Shape shape)
        {
            this.canvas = canvas;
            this.shape = shape;
        }

        public void Execute()
        {
            canvas.Children.Add(shape);
        }

        public void Undo()
        {
            canvas.Children.Remove(shape);
        }
    }

    public class DeleteShapeCommand : ICommand
    {
        private System.Windows.Controls.Canvas canvas;
        private Shape shape;

        public DeleteShapeCommand(System.Windows.Controls.Canvas canvas, Shape shape)
        {
            this.canvas = canvas;
            this.shape = shape;
        }

        public void Execute()
        {
            canvas.Children.Remove(shape);
        }

        public void Undo()
        {
            canvas.Children.Add(shape);
        }
    }
}
