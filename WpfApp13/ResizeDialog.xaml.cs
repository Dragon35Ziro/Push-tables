using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.IO;
using Newtonsoft.Json;

namespace TablePlacer
{
    public partial class MainWindow : Window
    {
        private const char EMPTY = '.';
        private const char TABLE = 'S';
        private const char CHAIR = 'h';
        private const char ENTRANCE = 'E';
        private const char WALL = 'W';
        private const int CELL_SIZE = 28;

        private char[,] room;
        private int entranceX, entranceY;
        private List<(int X, int Y, string Template)> placedTables = new List<(int, int, string)>();
        private Dictionary<int, int> selectedTables = new Dictionary<int, int>();
        private readonly (int dX, int dY)[] directions = { (0, 1), (1, 0), (0, -1), (-1, 0) };
        private Random random = new Random();
        private bool isMouseDown = false;
        private int movingTableIndex = -1;
        private Point dragStartPoint;
        private (int X, int Y, string Template) originalTablePosition;
        private List<UIElement> movingElements = new List<UIElement>();

        public MainWindow()
        {
            InitializeComponent();
            sliderZoom.Value = 1.0;
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int newWidth = int.Parse(tbWidth.Text);
                int newHeight = int.Parse(tbHeight.Text);
                int newEntranceX = int.Parse(tbEntranceX.Text);
                int newEntranceY = int.Parse(tbEntranceY.Text);

                if (newEntranceX < 0 || newEntranceY < 0)
                    throw new ArgumentException("Координаты входа не могут быть отрицательными!");

                if (room == null)
                {
                    InitializeRoom(newWidth, newHeight);
                }
                else if (newWidth != room.GetLength(1) || newHeight != room.GetLength(0))
                {
                    ResizeRoomWithWalls(newWidth, newHeight);
                }

                if (!IsInBounds(newEntranceX, newEntranceY))
                    throw new ArgumentException("Вход находится за пределами комнаты!");

                ClearDynamicObjects();

                entranceX = newEntranceX;
                entranceY = newEntranceY;
                if (room[entranceY, entranceX] == WALL)
                    throw new ArgumentException("Вход не может быть на стене!");
                room[entranceY, entranceX] = ENTRANCE;

                selectedTables.Clear();
                if (cbType1.IsChecked == true) selectedTables[1] = int.Parse(tbCount1.Text);
                if (cbType2.IsChecked == true) selectedTables[2] = int.Parse(tbCount2.Text);
                if (cbType3.IsChecked == true) selectedTables[3] = int.Parse(tbCount3.Text);

                bool success = await Task.Run(() => PlaceTablesInOrder());
                tbStatus.Text = success ? "Все столы размещены!" : "Не удалось разместить все столы!";
                DrawRoom();
            }
            catch (Exception ex)
            {
                tbStatus.Text = $"Ошибка: {ex.Message}";
            }
        }

        private bool PlaceTablesInOrder()
        {
            foreach (var tableType in selectedTables.Keys)
            {
                int remaining = selectedTables[tableType];
                int attempts = 0;
                const int maxAttempts = 10000; //100

                while (remaining > 0 && attempts < maxAttempts)
                {
                    attempts++;
                    if (TryPlaceTable(tableType))
                    {
                        remaining--;
                        attempts = 0;
                    }
                    else if (!RearrangePreviousTables())
                    {
                        return false;
                    }
                }

                if (attempts >= maxAttempts) return false;
            }
            return true;
        }

        private bool TryPlaceTable(int tableType)
        {
            string template = SelectTableTemplate(tableType);
            for (int attempt = 0; attempt < 1000; attempt++) //50
            {
                int x = random.Next(room.GetLength(1));
                int y = random.Next(room.GetLength(0));

                if (CheckPlacability(x, y, template))
                {
                    PlaceTable(x, y, template);
                    if (CheckReachability())
                    {
                        UpdateUI();
                        return true;
                    }
                    RemoveTable(x, y, template);
                }
            }
            return false;
        }

        private bool CheckPlacability(int x, int y, string template)
        {
            string[] rows = template.Split('\n');
            if (x + rows[0].Length > room.GetLength(1) || y + rows.Length > room.GetLength(0))
                return false;

            for (int dy = 0; dy < rows.Length; dy++)
            {
                for (int dx = 0; dx < rows[dy].Length; dx++)
                {
                    char cell = room[y + dy, x + dx];
                    if (rows[dy][dx] != EMPTY && cell != EMPTY)
                        return false;
                }
            }
            return true;
        }

        private void PlaceTable(int x, int y, string template)
        {
            string[] rows = template.Split('\n');
            for (int dy = 0; dy < rows.Length; dy++)
            {
                for (int dx = 0; dx < rows[dy].Length; dx++)
                {
                    if (rows[dy][dx] != EMPTY)
                        room[y + dy, x + dx] = rows[dy][dx];
                }
            }
            placedTables.Add((x, y, template));
        }

        private bool CheckReachability()
        {
            var chairs = CollectAllChairs();
            if (chairs.Count == 0) return true;

            bool[,] visited = new bool[room.GetLength(0), room.GetLength(1)];
            BFSFromEntrance(visited);

            return chairs.All(c => HasReachableNeighbor(c.X, c.Y, visited));
        }

        private void BFSFromEntrance(bool[,] visited)
        {
            var queue = new Queue<(int X, int Y)>();
            queue.Enqueue((entranceX, entranceY));
            visited[entranceY, entranceX] = true;

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                foreach (var (dx, dy) in directions)
                {
                    int nx = x + dx;
                    int ny = y + dy;

                    if (IsInBounds(nx, ny) && !visited[ny, nx] && room[ny, nx] == EMPTY)
                    {
                        visited[ny, nx] = true;
                        queue.Enqueue((nx, ny));
                    }
                }
            }
        }

        private bool HasReachableNeighbor(int x, int y, bool[,] visited)
        {
            foreach (var (dx, dy) in directions)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (IsInBounds(nx, ny) && visited[ny, nx])
                    return true;
            }
            return false;
        }

        private List<(int X, int Y)> CollectAllChairs()
        {
            var chairs = new List<(int X, int Y)>();
            foreach (var table in placedTables)
            {
                string[] rows = table.Template.Split('\n');
                for (int dy = 0; dy < rows.Length; dy++)
                {
                    for (int dx = 0; dx < rows[dy].Length; dx++)
                    {
                        if (rows[dy][dx] == CHAIR)
                            chairs.Add((table.X + dx, table.Y + dy));
                    }
                }
            }
            return chairs;
        }

        private bool RearrangePreviousTables()
        {
            for (int i = placedTables.Count - 1; i >= 0; i--)
            {
                var (x, y, template) = placedTables[i];
                RemoveTable(x, y, template);

                for (int attempt = 0; attempt < 10000; attempt++) //20
                {
                    int nx = random.Next(room.GetLength(1));
                    int ny = random.Next(room.GetLength(0));

                    if (CheckPlacability(nx, ny, template))
                    {
                        PlaceTable(nx, ny, template);
                        if (CheckReachability())
                        {
                            UpdateUI();
                            return true;
                        }
                        RemoveTable(nx, ny, template);
                    }
                }
                PlaceTable(x, y, template);
            }
            return false;
        }

        private string SelectTableTemplate(int tableType)
        {
            List<string> templates = GetTemplatesForTableType(tableType);
            return templates[random.Next(templates.Count)];
        }

        private List<string> GetTemplatesForTableType(int tableType)
        {
            switch (tableType)
            {
                case 1: return GenerateAllOrientations("SS", "Sh");
                case 2: return GenerateAllOrientations("SS", ".h");
                case 3: return GenerateAllOrientations("Sh", "..");
                default: throw new ArgumentException("Неизвестный тип стола");
            }
        }

        private List<string> GenerateAllOrientations(params string[] templateRows)
        {
            var orientations = new List<string>
            {
                string.Join("\n", templateRows),
                Rotate90(templateRows),
                Rotate180(templateRows),
                Rotate270(templateRows),
                MirrorHorizontal(templateRows),
                MirrorVertical(templateRows)
            };
            return orientations.Distinct().ToList();
        }

        private string Rotate90(string[] templateRows)
        {
            int rows = templateRows.Length;
            int cols = templateRows[0].Length;
            var rotated = new char[cols, rows];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    rotated[c, rows - 1 - r] = templateRows[r][c];
            return ArrayToString(rotated);
        }

        private string Rotate180(string[] templateRows)
        {
            return MirrorHorizontal(MirrorVertical(templateRows).Split('\n'));
        }

        private string Rotate270(string[] templateRows)
        {
            return Rotate90(Rotate180(templateRows).Split('\n'));
        }

        private string MirrorHorizontal(string[] templateRows)
        {
            return string.Join("\n", templateRows.Select(row => new string(row.Reverse().ToArray())));
        }

        private string MirrorVertical(string[] templateRows)
        {
            return string.Join("\n", templateRows.Reverse());
        }

        private string ArrayToString(char[,] array)
        {
            var sb = new System.Text.StringBuilder();
            for (int y = 0; y < array.GetLength(0); y++)
            {
                for (int x = 0; x < array.GetLength(1); x++)
                {
                    sb.Append(array[y, x]);
                }
                if (y < array.GetLength(0) - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        private void InitializeRoom(int width, int height)
        {
            room = new char[height, width];
            canvasRoom.Width = width * CELL_SIZE;
            canvasRoom.Height = height * CELL_SIZE;

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    room[y, x] = EMPTY;
        }

        private void RemoveTable(int x, int y, string template)
        {
            string[] rows = template.Split('\n');
            for (int dy = 0; dy < rows.Length; dy++)
            {
                for (int dx = 0; dx < rows[dy].Length; dx++)
                {
                    int cellX = x + dx;
                    int cellY = y + dy;
                    if (IsInBounds(cellX, cellY) && rows[dy][dx] != EMPTY)
                        room[cellY, cellX] = EMPTY;
                }
            }
            placedTables.RemoveAll(t => t.X == x && t.Y == y && t.Template == template);
        }

        private bool IsInBounds(int x, int y)
        {
            return room != null
                && x >= 0
                && x < room.GetLength(1)
                && y >= 0
                && y < room.GetLength(0);
        }

        private void DrawRoom()
        {
            canvasRoom.Children.Clear();
            for (int y = 0; y < room.GetLength(0); y++)
            {
                for (int x = 0; x < room.GetLength(1); x++)
                {
                    var cell = new Border
                    {
                        Width = CELL_SIZE,
                        Height = CELL_SIZE,
                        BorderBrush = Brushes.Gray,
                        BorderThickness = new Thickness(0.3),
                        Background = GetCellBrush(room[y, x])
                    };

                    Canvas.SetLeft(cell, x * CELL_SIZE);
                    Canvas.SetTop(cell, y * CELL_SIZE);

                    if (room[y, x] == TABLE || room[y, x] == CHAIR)
                    {
                        for (int i = 0; i < placedTables.Count; i++)
                        {
                            var table = placedTables[i];
                            string[] rows = table.Template.Split('\n');
                            int dx = x - table.X;
                            int dy = y - table.Y;
                            if (dx >= 0 && dx < rows[0].Length &&
                                dy >= 0 && dy < rows.Length &&
                                rows[dy][dx] != EMPTY)
                            {
                                cell.Tag = i;
                                break;
                            }
                        }
                    }

                    canvasRoom.Children.Add(cell);
                }
            }
        }

        private Brush GetCellBrush(char cell)
        {
            switch (cell)
            {
                case TABLE: return Brushes.SaddleBrown;
                case CHAIR: return Brushes.LimeGreen;
                case ENTRANCE: return Brushes.Red;
                case WALL: return Brushes.DarkSlateGray;
                default: return Brushes.White;
            }
        }

        private void UpdateUI()
        {
            Dispatcher.Invoke(DrawRoom);
        }

        private void ClearDynamicObjects()
        {
            for (int y = 0; y < room.GetLength(0); y++)
            {
                for (int x = 0; x < room.GetLength(1); x++)
                {
                    if (room[y, x] == TABLE ||
                        room[y, x] == CHAIR ||
                        room[y, x] == ENTRANCE)
                    {
                        room[y, x] = EMPTY;
                    }
                }
            }
            placedTables.Clear();
            canvasRoom.Children.Clear();
        }

        private void ResizeRoomWithWalls(int newWidth, int newHeight)
        {
            if (room == null) return;

            char[,] newRoom = new char[newHeight, newWidth];

            for (int y = 0; y < Math.Min(newHeight, room.GetLength(0)); y++)
            {
                for (int x = 0; x < Math.Min(newWidth, room.GetLength(1)); x++)
                {
                    newRoom[y, x] = room[y, x] == WALL ? WALL : EMPTY;
                }
            }

            room = newRoom;
            canvasRoom.Width = newWidth * CELL_SIZE;
            canvasRoom.Height = newHeight * CELL_SIZE;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (room == null) return;

                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    DefaultExt = ".json"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var layout = new SavedLayout
                    {
                        Width = room.GetLength(1),
                        Height = room.GetLength(0),
                        EntranceX = entranceX,
                        EntranceY = entranceY,
                        SelectedTables = selectedTables,
                        PlacedTables = placedTables.Select(t => new TablePosition
                        {
                            X = t.X,
                            Y = t.Y,
                            Template = t.Template
                        }).ToList(),
                        Room = (char[,])room.Clone()
                    };

                    string json = JsonConvert.SerializeObject(layout, Formatting.Indented);
                    File.WriteAllText(saveFileDialog.FileName, json);
                    tbStatus.Text = "План успешно сохранен!";
                }
            }
            catch (Exception ex)
            {
                tbStatus.Text = $"Ошибка сохранения: {ex.Message}";
            }
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    DefaultExt = ".json"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string json = File.ReadAllText(openFileDialog.FileName);
                    var layout = JsonConvert.DeserializeObject<SavedLayout>(json);

                    InitializeRoom(layout.Width, layout.Height);
                    entranceX = layout.EntranceX;
                    entranceY = layout.EntranceY;
                    room = layout.Room;
                    placedTables = layout.PlacedTables.Select(t => (t.X, t.Y, t.Template)).ToList();

                    tbWidth.Text = layout.Width.ToString();
                    tbHeight.Text = layout.Height.ToString();
                    tbEntranceX.Text = layout.EntranceX.ToString();
                    tbEntranceY.Text = layout.EntranceY.ToString();

                    cbType1.IsChecked = layout.SelectedTables.ContainsKey(1);
                    cbType2.IsChecked = layout.SelectedTables.ContainsKey(2);
                    cbType3.IsChecked = layout.SelectedTables.ContainsKey(3);

                    if (layout.SelectedTables.ContainsKey(1)) tbCount1.Text = layout.SelectedTables[1].ToString();
                    if (layout.SelectedTables.ContainsKey(2)) tbCount2.Text = layout.SelectedTables[2].ToString();
                    if (layout.SelectedTables.ContainsKey(3)) tbCount3.Text = layout.SelectedTables[3].ToString();

                    DrawRoom();
                    tbStatus.Text = "План успешно загружен!";
                }
            }
            catch (Exception ex)
            {
                tbStatus.Text = $"Ошибка загрузки: {ex.Message}";
            }
        }

        private void CanvasRoom_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isMouseDown = true;
            ProcessCanvasClick(e.GetPosition(canvasRoom));

            if (rbMoveTables.IsChecked != true) return;

            var pos = e.GetPosition(canvasRoom);
            int x = (int)(pos.X / CELL_SIZE);
            int y = (int)(pos.Y / CELL_SIZE);

            var element = canvasRoom.Children
                .OfType<Border>()
                .FirstOrDefault(b =>
                    (int)(Canvas.GetLeft(b) / CELL_SIZE) == x &&
                    (int)(Canvas.GetTop(b) / CELL_SIZE) == y &&
                    b.Tag != null);

            if (element?.Tag is int tableIndex)
            {
                movingTableIndex = tableIndex;
                originalTablePosition = placedTables[tableIndex];
                dragStartPoint = pos;

                movingElements = canvasRoom.Children
                    .OfType<Border>()
                    .Where(b => b.Tag is int idx && idx == tableIndex)
                    .Cast<UIElement>()
                    .ToList();

                foreach (var el in movingElements)
                {
                    el.Opacity = 0.7;
                    el.RenderTransform = new TranslateTransform();
                }
            }
        }

        private void CanvasRoom_MouseMove(object sender, MouseEventArgs e)
        {
            if (isMouseDown)
            {
                ProcessCanvasClick(e.GetPosition(canvasRoom));
            }
            else if (movingTableIndex != -1)
            {
                var currentPos = e.GetPosition(canvasRoom);
                double deltaX = currentPos.X - dragStartPoint.X;
                double deltaY = currentPos.Y - dragStartPoint.Y;

                foreach (var element in movingElements)
                {
                    var transform = element.RenderTransform as TranslateTransform;
                    transform.X = deltaX;
                    transform.Y = deltaY;
                }
            }
        }

        private void CanvasRoom_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isMouseDown = false;

            if (movingTableIndex == -1) return;

            foreach (var el in movingElements)
            {
                el.Opacity = 1;
                el.RenderTransform = null;
            }

            var currentPos = e.GetPosition(canvasRoom);
            int newX = originalTablePosition.X + (int)((currentPos.X - dragStartPoint.X) / CELL_SIZE);
            int newY = originalTablePosition.Y + (int)((currentPos.Y - dragStartPoint.Y) / CELL_SIZE);

            var template = originalTablePosition.Template;

            if (CanPlaceTable(newX, newY, template, originalTablePosition.X, originalTablePosition.Y))
            {
                RemoveTable(originalTablePosition.X, originalTablePosition.Y, template);
                PlaceTable(newX, newY, template);
                placedTables[movingTableIndex] = (newX, newY, template);

                if (!CheckReachability())
                {
                    RemoveTable(newX, newY, template);
                    PlaceTable(originalTablePosition.X, originalTablePosition.Y, template);
                    placedTables[movingTableIndex] = originalTablePosition;
                    MessageBox.Show("Невозможно переместить стол - нарушена доступность кресел!",
                                  "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            movingElements.Clear();
            movingTableIndex = -1;
            DrawRoom();
        }

        private bool CanPlaceTable(int newX, int newY, string template, int originalX, int originalY)
        {
            char[,] tempRoom = (char[,])room.Clone();
            RemoveTableFromTemp(tempRoom, originalX, originalY, template);
            return CheckPlacability(tempRoom, newX, newY, template);
        }

        private void RemoveTableFromTemp(char[,] tempRoom, int x, int y, string template)
        {
            string[] rows = template.Split('\n');
            for (int dy = 0; dy < rows.Length; dy++)
            {
                for (int dx = 0; dx < rows[dy].Length; dx++)
                {
                    if (rows[dy][dx] != EMPTY)
                    {
                        int cellX = x + dx;
                        int cellY = y + dy;
                        if (IsInBounds(cellX, cellY))
                            tempRoom[cellY, cellX] = EMPTY;
                    }
                }
            }
        }

        private bool CheckPlacability(char[,] tempRoom, int x, int y, string template)
        {
            string[] rows = template.Split('\n');
            int tWidth = rows[0].Length;
            int tHeight = rows.Length;

            if (x < 0 || y < 0 || x + tWidth > tempRoom.GetLength(1) || y + tHeight > tempRoom.GetLength(0))
                return false;

            for (int dy = 0; dy < tHeight; dy++)
            {
                for (int dx = 0; dx < tWidth; dx++)
                {
                    if (rows[dy][dx] != EMPTY && tempRoom[y + dy, x + dx] != EMPTY)
                        return false;
                }
            }
            return true;
        }

        private void BtnResizeRoom_Click(object sender, RoutedEventArgs e)
        {
            if (room == null) return;

            var dialog = new ResizeDialog(room.GetLength(1), room.GetLength(0));
            if (dialog.ShowDialog() == true) ResizeRoomWithWalls(dialog.NewWidth, dialog.NewHeight);
        }

        private void ProcessCanvasClick(Point position)
        {
            if (room == null) return;

            int x = (int)(position.X / CELL_SIZE);
            int y = (int)(position.Y / CELL_SIZE);

            if (!IsInBounds(x, y)) return;

            if (rbAddWalls.IsChecked == true)
            {
                if (room[y, x] == EMPTY)
                {
                    room[y, x] = WALL;
                    DrawRoom();
                }
            }
            else if (rbRemoveWalls.IsChecked == true)
            {
                if (room[y, x] == WALL)
                {
                    room[y, x] = EMPTY;
                    DrawRoom();
                }
            }
        }
    }

    public class SavedLayout
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int EntranceX { get; set; }
        public int EntranceY { get; set; }
        public Dictionary<int, int> SelectedTables { get; set; }
        public List<TablePosition> PlacedTables { get; set; }
        public char[,] Room { get; set; }
    }

    public class TablePosition
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string Template { get; set; }
    }
}