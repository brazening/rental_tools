using System;
using System.Collections.Generic;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using Npgsql;

namespace Rental
{
    public partial class Movement : Window
    {
        private string connString;
        private int currentEmployeeId = 1;

        public Movement()
        {
            InitializeComponent();
            var app = (App)Application.Current;
            connString = app.connString;

            dpMovementDate.SelectedDate = DateTime.Today;

            LoadEmployees();
            LoadStocks();
            LoadTools();
            LoadMovements();
        }

        /// <summary>
        /// Получить информацию о заполненности склада (только активные инструменты)
        /// </summary>
        private (int current, int max) GetStockCapacity(int stockId, int? excludeToolId = null)
        {
            string query = @"
                SELECT COUNT(*) 
                FROM public.tools 
                WHERE stock_id = @stockId 
                AND status NOT IN ('списан', 'утерян')";

            if (excludeToolId.HasValue)
            {
                query += " AND id != @excludeToolId";
            }

            try
            {
                using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@stockId", stockId);
                        if (excludeToolId.HasValue)
                            cmd.Parameters.AddWithValue("@excludeToolId", excludeToolId.Value);

                        int currentCount = Convert.ToInt32(cmd.ExecuteScalar());

                        string maxQuery = "SELECT max_quantity FROM public.stock WHERE id = @stockId";
                        using (NpgsqlCommand maxCmd = new NpgsqlCommand(maxQuery, conn))
                        {
                            maxCmd.Parameters.AddWithValue("@stockId", stockId);
                            int maxQuantity = Convert.ToInt32(maxCmd.ExecuteScalar());

                            return (currentCount, maxQuantity);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка проверки склада: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return (0, 0);
            }
        }

        /// <summary>
        /// Проверка, можно ли переместить инструмент на склад назначения
        /// </summary>
        private bool CanMoveToStock(int toStockId, int toolId, int? fromStockId = null)
        {
            var (current, max) = GetStockCapacity(toStockId, toolId);

            if (current >= max)
            {
                MessageBox.Show($"Невозможно переместить инструмент!\n\n" +
                    $"Склад \"{GetStockName(toStockId)}\" переполнен.\n" +
                    $"Максимальная вместимость: {max} инструментов\n" +
                    $"Текущее количество активных инструментов: {current}\n" +
                    $"Свободных мест: 0\n\n" +
                    $"Пожалуйста, выберите другой склад для перемещения.",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            int remaining = max - current;
            if (remaining <= 2)
            {
                var result = MessageBox.Show($"ВНИМАНИЕ: На складе \"{GetStockName(toStockId)}\" осталось всего {remaining} свободных мест из {max}.\n\n" +
                    $"Инструмент: {GetToolInventoryNumber(toolId)}\n" +
                    $"Перемещение возможно, но склад почти заполнен.\n\n" +
                    $"Продолжить перемещение?",
                    "Предупреждение", MessageBoxButton.YesNo, MessageBoxImage.Question);

                return result == MessageBoxResult.Yes;
            }

            return true;
        }

        /// <summary>
        /// Получить название склада по ID
        /// </summary>
        private string GetStockName(int stockId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = "SELECT name FROM public.stock WHERE id = @id";
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", stockId);
                        var result = cmd.ExecuteScalar();
                        return result != null ? result.ToString() : "Неизвестный склад";
                    }
                }
            }
            catch
            {
                return "Неизвестный склад";
            }
        }

        /// <summary>
        /// Получить инвентарный номер инструмента
        /// </summary>
        private string GetToolInventoryNumber(int toolId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = "SELECT inventory_number FROM public.tools WHERE id = @id";
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", toolId);
                        var result = cmd.ExecuteScalar();
                        return result != null ? result.ToString() : "Неизвестный инструмент";
                    }
                }
            }
            catch
            {
                return "Неизвестный инструмент";
            }
        }

        private void LoadEmployees()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"
                        SELECT id, fio, role 
                        FROM public.employees 
                        ORDER BY fio";

                    var adapter = new NpgsqlDataAdapter(query, conn);
                    var dt = new DataTable();
                    adapter.Fill(dt);

                    cmbEmployee.ItemsSource = dt.DefaultView;

                    if (currentEmployeeId > 0)
                    {
                        foreach (DataRowView row in cmbEmployee.Items)
                        {
                            if ((int)row["id"] == currentEmployeeId)
                            {
                                cmbEmployee.SelectedItem = row;
                                break;
                            }
                        }
                    }

                    if (cmbEmployee.SelectedItem == null && cmbEmployee.Items.Count > 0)
                    {
                        cmbEmployee.SelectedIndex = 0;
                        if (cmbEmployee.SelectedItem is DataRowView selectedRow)
                        {
                            currentEmployeeId = (int)selectedRow["id"];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки сотрудников: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadStocks()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"
                        SELECT s.id, s.name, s.max_quantity, 
                               COUNT(t.id) as current_quantity
                        FROM public.stock s
                        LEFT JOIN public.tools t ON t.stock_id = s.id 
                            AND t.status NOT IN ('списан', 'утерян')
                        GROUP BY s.id, s.name, s.max_quantity
                        ORDER BY s.name";

                    var adapter = new NpgsqlDataAdapter(query, conn);
                    var dt = new DataTable();
                    adapter.Fill(dt);

                    // Исправлено: создаем новый DataTable для отображения
                    var displayDt = new DataTable();
                    displayDt.Columns.Add("Id", typeof(int));
                    displayDt.Columns.Add("DisplayName", typeof(string));

                    foreach (DataRow row in dt.Rows)
                    {
                        int id = Convert.ToInt32(row["id"]);
                        int maxQuantity = Convert.ToInt32(row["max_quantity"]);
                        int currentQuantity = Convert.ToInt32(row["current_quantity"]);
                        string name = row["name"].ToString();
                        int remaining = maxQuantity - currentQuantity;
                        string displayName = $"{name} ({currentQuantity}/{maxQuantity}, свободно: {remaining})";

                        displayDt.Rows.Add(id, displayName);
                    }

                    cmbFromStock.DisplayMemberPath = "DisplayName";
                    cmbFromStock.SelectedValuePath = "Id";
                    cmbFromStock.ItemsSource = displayDt.DefaultView;

                    cmbToStock.DisplayMemberPath = "DisplayName";
                    cmbToStock.SelectedValuePath = "Id";
                    cmbToStock.ItemsSource = displayDt.DefaultView;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки складов: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadTools()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"
                        SELECT t.id, t.inventory_number, tm.name as model_name, 
                               t.stock_id, s.name as stock_name,
                               t.status
                        FROM public.tools t
                        JOIN public.tool_models tm ON t.model_id = tm.id
                        LEFT JOIN public.stock s ON t.stock_id = s.id
                        WHERE t.status != 'списан' 
                          AND t.status != 'утерян'
                          AND t.status != 'в аренде'
                          AND t.status != 'в ремонте'
                        ORDER BY tm.name, t.inventory_number";

                    var cmd = new NpgsqlCommand(query, conn);
                    var reader = cmd.ExecuteReader();

                    var tools = new List<ToolInfo>();
                    while (reader.Read())
                    {
                        var tool = new ToolInfo();
                        tool.Id = reader.GetInt32(0);
                        tool.InventoryNumber = reader.GetString(1);
                        tool.ModelName = reader.GetString(2);
                        tool.Status = reader.GetString(5); // status

                        if (reader.IsDBNull(3))
                            tool.StockId = null;
                        else
                            tool.StockId = reader.GetInt32(3);

                        if (reader.IsDBNull(4))
                            tool.StockName = "Не указан";
                        else
                            tool.StockName = reader.GetString(4);

                        tools.Add(tool);
                    }
                    reader.Close();

                    cmbTool.ItemsSource = tools;

                    txtStatus.Text = $"Загружено инструментов: {tools.Count} (доступные для перемещения)";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки инструментов: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CmbTool_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbTool.SelectedItem != null)
            {
                var selected = (ToolInfo)cmbTool.SelectedItem;
                txtCurrentStock.Text = selected.StockName;

                if (selected.StockId.HasValue)
                {
                    // Устанавливаем выбранный склад в cmbFromStock
                    foreach (var item in cmbFromStock.ItemsSource)
                    {
                        var row = (DataRowView)item;
                        if (Convert.ToInt32(row["Id"]) == selected.StockId.Value)
                        {
                            cmbFromStock.SelectedItem = item;
                            break;
                        }
                    }
                }
                else
                {
                    cmbFromStock.SelectedIndex = -1;
                }

                txtStatus.Text = $"Выбран инструмент: {selected.InventoryNumber} - {selected.ModelName} | Статус: {selected.Status} | Текущий склад: {selected.StockName}";
            }
        }

        private void LoadMovements()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"
                        SELECT sm.id, sm.movement_date, 
                               t.inventory_number, tm.name as model_name,
                               fs.name as from_stock_name, ts.name as to_stock_name,
                               e.fio as employee_fio
                        FROM public.stock_movements sm
                        JOIN public.tools t ON sm.tool_id = t.id
                        JOIN public.tool_models tm ON t.model_id = tm.id
                        LEFT JOIN public.stock fs ON sm.from_stock_id = fs.id
                        JOIN public.stock ts ON sm.to_stock_id = ts.id
                        LEFT JOIN public.employees e ON sm.employee_id = e.id
                        ORDER BY sm.movement_date DESC";

                    var cmd = new NpgsqlCommand(query, conn);
                    var reader = cmd.ExecuteReader();
                    var movements = new List<MovementViewModel>();

                    while (reader.Read())
                    {
                        var mv = new MovementViewModel();
                        mv.Id = reader.GetInt32(0);
                        mv.MovementDate = reader.GetDateTime(1);
                        mv.InventoryNumber = reader.GetString(2);
                        mv.ModelName = reader.GetString(3);

                        if (reader.IsDBNull(4))
                            mv.FromStockName = "—";
                        else
                            mv.FromStockName = reader.GetString(4);

                        mv.ToStockName = reader.GetString(5);

                        if (reader.IsDBNull(6))
                            mv.EmployeeFio = "—";
                        else
                            mv.EmployeeFio = reader.GetString(6);

                        movements.Add(mv);
                    }
                    reader.Close();

                    dgMovements.ItemsSource = movements;
                    txtStatus.Text = $"Загружено перемещений: {movements.Count}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки перемещений: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveMovement()
        {
            if (!ValidateForm())
                return;

            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (var transaction = conn.BeginTransaction())
                    {
                        var selectedTool = (ToolInfo)cmbTool.SelectedItem;

                        // Получаем выбранный склад назначения
                        int toStockId;
                        if (cmbToStock.SelectedItem is DataRowView selectedToStock)
                        {
                            toStockId = Convert.ToInt32(selectedToStock["Id"]);
                        }
                        else
                        {
                            MessageBox.Show("Выберите склад назначения!", "Ошибка",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            transaction.Rollback();
                            return;
                        }

                        // Получаем текущий склад из БД
                        int? currentStockId = null;
                        string getCurrentStockQuery = "SELECT stock_id FROM public.tools WHERE id = @id";
                        using (var getStockCmd = new NpgsqlCommand(getCurrentStockQuery, conn, transaction))
                        {
                            getStockCmd.Parameters.AddWithValue("@id", selectedTool.Id);
                            var result = getStockCmd.ExecuteScalar();
                            if (result != DBNull.Value && result != null)
                                currentStockId = Convert.ToInt32(result);
                        }

                        // ПРОВЕРКА ЗАПОЛНЕННОСТИ СКЛАДА НАЗНАЧЕНИЯ
                        if (!CanMoveToStock(toStockId, selectedTool.Id, currentStockId))
                        {
                            transaction.Rollback();
                            return;
                        }

                        // Получаем выбранного сотрудника
                        int employeeId;
                        if (cmbEmployee.SelectedItem is DataRowView selectedEmployee)
                        {
                            employeeId = (int)selectedEmployee["id"];
                        }
                        else
                        {
                            MessageBox.Show("Выберите сотрудника, выполняющего перемещение!", "Предупреждение",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            transaction.Rollback();
                            return;
                        }

                        DateTime movementDate = DateTime.Today;
                        if (dpMovementDate.SelectedDate.HasValue)
                        {
                            movementDate = dpMovementDate.SelectedDate.Value;
                        }

                        // Записываем перемещение
                        string insertQuery = @"
                            INSERT INTO public.stock_movements 
                            (tool_id, from_stock_id, to_stock_id, movement_date, employee_id)
                            VALUES (@tool_id, @from_stock, @to_stock, @date, @employee)
                            RETURNING id";

                        var cmd = new NpgsqlCommand(insertQuery, conn, transaction);
                        cmd.Parameters.AddWithValue("@tool_id", selectedTool.Id);

                        if (currentStockId.HasValue)
                            cmd.Parameters.AddWithValue("@from_stock", currentStockId.Value);
                        else
                            cmd.Parameters.AddWithValue("@from_stock", DBNull.Value);

                        cmd.Parameters.AddWithValue("@to_stock", toStockId);
                        cmd.Parameters.AddWithValue("@date", movementDate);
                        cmd.Parameters.AddWithValue("@employee", employeeId);

                        int movementId = (int)cmd.ExecuteScalar();

                        // Обновляем склад инструмента
                        string updateTool = @"
                            UPDATE public.tools 
                            SET stock_id = @to_stock 
                            WHERE id = @tool_id";

                        var cmdUpdate = new NpgsqlCommand(updateTool, conn, transaction);
                        cmdUpdate.Parameters.AddWithValue("@to_stock", toStockId);
                        cmdUpdate.Parameters.AddWithValue("@tool_id", selectedTool.Id);
                        cmdUpdate.ExecuteNonQuery();

                        transaction.Commit();

                        // Получаем названия складов для сообщения
                        string fromStockName = currentStockId.HasValue ? GetStockName(currentStockId.Value) : "—";
                        string toStockName = GetStockName(toStockId);

                        MessageBox.Show($"Перемещение выполнено сотрудником: {selectedEmployee["fio"]}!\n\n" +
                            $"Детали перемещения:\n" +
                            $"Инструмент: {selectedTool.InventoryNumber} - {selectedTool.ModelName}\n" +
                            $"Со склада: {fromStockName}\n" +
                            $"На склад: {toStockName}\n" +
                            $"Дата: {movementDate:dd.MM.yyyy}",
                            "Успех", MessageBoxButton.OK, MessageBoxImage.Information);

                        ClearForm();
                        LoadMovements();
                        LoadTools();
                        LoadStocks();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidateForm()
        {
            if (cmbTool.SelectedItem == null)
            {
                MessageBox.Show("Выберите инструмент!", "Предупреждение",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (cmbToStock.SelectedItem == null)
            {
                MessageBox.Show("Выберите склад назначения!", "Предупреждение",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (cmbEmployee.SelectedItem == null)
            {
                MessageBox.Show("Выберите сотрудника, выполняющего перемещение!", "Предупреждение",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var selectedTool = (ToolInfo)cmbTool.SelectedItem;

            if (selectedTool.Status == "списан" || selectedTool.Status == "утерян")
            {
                MessageBox.Show($"Инструмент со статусом \"{selectedTool.Status}\" нельзя перемещать!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // Получаем ID склада назначения
            int toStockId;
            if (cmbToStock.SelectedItem is DataRowView selectedToStock)
            {
                toStockId = Convert.ToInt32(selectedToStock["Id"]);
            }
            else
            {
                MessageBox.Show("Ошибка получения склада назначения!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // Проверяем, не пытаемся ли переместить на тот же склад
            if (selectedTool.StockId == toStockId)
            {
                MessageBox.Show("Инструмент уже находится на выбранном складе!", "Предупреждение",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void ClearForm()
        {
            cmbTool.SelectedItem = null;
            cmbFromStock.SelectedIndex = -1;
            cmbToStock.SelectedIndex = -1;
            dpMovementDate.SelectedDate = DateTime.Today;
            txtCurrentStock.Text = "";

            if (cmbEmployee.SelectedItem == null && cmbEmployee.Items.Count > 0)
            {
                cmbEmployee.SelectedIndex = 0;
            }

            txtStatus.Text = "Форма очищена. Готов к перемещению.";
        }

        private void BtnPost_Click(object sender, RoutedEventArgs e)
        {
            SaveMovement();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
        }
    }

    public class ToolInfo
    {
        public int Id { get; set; }
        public string InventoryNumber { get; set; }
        public string ModelName { get; set; }
        public int? StockId { get; set; }
        public string StockName { get; set; }
        public string Status { get; set; }
    }

    public class MovementViewModel
    {
        public int Id { get; set; }
        public DateTime MovementDate { get; set; }
        public string InventoryNumber { get; set; }
        public string ModelName { get; set; }
        public string FromStockName { get; set; }
        public string ToStockName { get; set; }
        public string EmployeeFio { get; set; }
    }
}