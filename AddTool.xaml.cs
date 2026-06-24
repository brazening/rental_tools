using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Npgsql;

namespace Rental
{
    public partial class AddTool : Window
    {
        private string connString;
        private ObservableCollection<ToolDisplay> toolsList;
        private ObservableCollection<ToolDisplay> filteredToolsList;
        private ObservableCollection<ModelInfo> modelsList;
        private ObservableCollection<StockInfo> stocksList;
        private int selectedId = -1;

        public AddTool()
        {
            InitializeComponent();

            var app = (App)Application.Current;
            connString = app.connString;

            toolsList = new ObservableCollection<ToolDisplay>();
            filteredToolsList = new ObservableCollection<ToolDisplay>();
            modelsList = new ObservableCollection<ModelInfo>();
            stocksList = new ObservableCollection<StockInfo>();
            dgTools.ItemsSource = filteredToolsList;

            LoadModels();
            LoadStocks();
            LoadTools();

            if (dpPurchaseDate != null)
                dpPurchaseDate.SelectedDate = DateTime.Now;
        }

        public class ToolDisplay
        {
            public int Id { get; set; }
            public string InventoryNumber { get; set; }
            public int? ModelId { get; set; }
            public string ModelName { get; set; }
            public string Status { get; set; }
            public string ConditionStatus { get; set; }
            public decimal? PurchasePrice { get; set; }
            public string CategoryName { get; set; }
            public DateTime? PurchaseDate { get; set; }
            public int AgeYears { get; set; }
            public string Discount { get; set; }
            public decimal DiscountPercent { get; set; }
            public string StockName { get; set; }
            public int? StockId { get; set; }
        }

        public class ModelInfo
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string CategoryName { get; set; }
        }

        public class StockInfo
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int MaxQuantity { get; set; }
            public int CurrentQuantity { get; set; }

            public string DisplayName
            {
                get
                {
                    int remaining = MaxQuantity - CurrentQuantity;
                    return $"{Name} ({CurrentQuantity}/{MaxQuantity}, свободно: {remaining})";
                }
            }
        }

        public string GetConnString()
        {
            return connString;
        }

        // Вычисление скидки по возрасту
        private decimal CalculateDiscountByAge(DateTime? purchaseDate)
        {
            if (!purchaseDate.HasValue) return 0;

            int ageYears = DateTime.Now.Year - purchaseDate.Value.Year;
            if (purchaseDate.Value > DateTime.Now.AddYears(-ageYears)) ageYears--;

            if (ageYears >= 6) return 20;
            if (ageYears >= 3) return 10;
            return 0;
        }

        // Вычисление возраста в годах
        private int CalculateAgeYears(DateTime? purchaseDate)
        {
            if (!purchaseDate.HasValue) return 0;

            int ageYears = DateTime.Now.Year - purchaseDate.Value.Year;
            if (purchaseDate.Value > DateTime.Now.AddYears(-ageYears)) ageYears--;
            return ageYears;
        }

        public string GenerateInventoryNumber()
        {
            if (cmbModel.SelectedItem == null)
                return "";

            ModelInfo selectedModel = (ModelInfo)cmbModel.SelectedItem;
            string prefix = selectedModel.Name.Length >= 3 ?
                            selectedModel.Name.Substring(0, 3).ToUpper() :
                            selectedModel.Name.ToUpper();

            string query = "SELECT COUNT(*) FROM public.tools WHERE model_id = @modelId";

            int count = 0;
            try
            {
                using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@modelId", selectedModel.Id);
                        count = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка генерации номера: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return $"{prefix}-{(count + 1):D3}";
        }

        // Проверка, есть ли место на складе
        private bool IsStockAvailable(int stockId, int? excludeToolId = null)
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

                            return currentCount < maxQuantity;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка проверки склада: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // Получить информацию о заполненности склада (текущее/максимум)
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
                MessageBox.Show($"Ошибка получения информации о складе: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return (0, 0);
            }
        }

        // Проверка, можно ли изменять склад у инструмента
        private bool CanChangeStock(int toolId, string currentStatus)
        {
            if (currentStatus == "доступен")
                return true;

            if (currentStatus == "в аренде" || currentStatus == "в ремонте" ||
                currentStatus == "списан" || currentStatus == "утерян")
                return false;

            return false;
        }

        // Блокировка/разблокировка выбора склада в зависимости от статуса
        private void SetStockComboBoxState(string status)
        {
            if (cmbStock == null) return;

            if (status == "доступен")
            {
                cmbStock.IsEnabled = true;
                txtStatus.Text = "Склад можно изменять";
            }
            else if (status == "в аренде")
            {
                cmbStock.IsEnabled = false;
                txtStatus.Text = "Инструмент в аренде - склад изменить нельзя!";
            }
            else if (status == "в ремонте")
            {
                cmbStock.IsEnabled = false;
                txtStatus.Text = "Инструмент в ремонте - склад изменить нельзя!";
            }
            else if (status == "списан")
            {
                cmbStock.IsEnabled = false;
                txtStatus.Text = "Инструмент списан - изменение невозможно!";
            }
            else if (status == "утерян")
            {
                cmbStock.IsEnabled = false;
                txtStatus.Text = "Инструмент утерян - изменение невозможно!";
            }
            else
            {
                cmbStock.IsEnabled = true;
            }
        }

        private void LoadStocks()
        {
            stocksList.Clear();
            string query = @"
                SELECT s.id, s.name, s.max_quantity, 
                       COUNT(t.id) as current_quantity
                FROM public.stock s
                LEFT JOIN public.tools t ON t.stock_id = s.id AND t.status NOT IN ('списан', 'утерян')
                GROUP BY s.id, s.name, s.max_quantity
                ORDER BY s.name";

            try
            {
                using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conn))
                    {
                        using (NpgsqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                StockInfo stock = new StockInfo();
                                stock.Id = reader.GetInt32(0);
                                stock.Name = reader.GetString(1);
                                stock.MaxQuantity = reader.GetInt32(2);
                                stock.CurrentQuantity = reader.GetInt32(3);
                                stocksList.Add(stock);
                            }
                        }
                    }
                }

                cmbStock.DisplayMemberPath = "DisplayName";
                cmbStock.ItemsSource = stocksList;
                cmbStock.SelectedIndex = -1;

                // Загрузка складов в фильтр
                if (cmbFilterStock != null)
                {
                    cmbFilterStock.Items.Clear();
                    cmbFilterStock.Items.Add(new ComboBoxItem { Content = "Все", Tag = null });
                    foreach (var stock in stocksList)
                    {
                        cmbFilterStock.Items.Add(new ComboBoxItem { Content = stock.Name, Tag = stock.Id });
                    }
                    cmbFilterStock.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки складов: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadModels()
        {
            modelsList.Clear();
            string query = @"
                SELECT tm.id, tm.name, c.name as category_name
                FROM public.tool_models tm
                LEFT JOIN public.categories c ON tm.category_id = c.id
                ORDER BY tm.name";

            try
            {
                using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conn))
                    {
                        using (NpgsqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                ModelInfo model = new ModelInfo();
                                model.Id = reader.GetInt32(0);
                                model.Name = reader.GetString(1);
                                model.CategoryName = reader.IsDBNull(2) ? "" : reader.GetString(2);
                                modelsList.Add(model);
                            }
                        }
                    }
                }
                cmbModel.ItemsSource = modelsList;
                cmbModel.SelectedIndex = -1;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки моделей: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadTools()
        {
            toolsList.Clear();
            string query = @"
                SELECT t.id, t.inventory_number, t.status, t.condition_status, 
                       t.purchase_price, t.purchase_date, t.stock_id,
                       tm.name as model_name, c.name as category_name, s.name as stock_name
                FROM public.tools t
                LEFT JOIN public.tool_models tm ON t.model_id = tm.id
                LEFT JOIN public.categories c ON tm.category_id = c.id
                LEFT JOIN public.stock s ON t.stock_id = s.id
                ORDER BY t.id";

            try
            {
                using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conn))
                    {
                        using (NpgsqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                ToolDisplay tool = new ToolDisplay();
                                tool.Id = reader.GetInt32(0);
                                tool.InventoryNumber = reader.GetString(1);
                                tool.Status = reader.GetString(2);
                                tool.ConditionStatus = reader.GetString(3);
                                tool.PurchasePrice = reader.IsDBNull(4) ? (decimal?)null : reader.GetDecimal(4);
                                tool.PurchaseDate = reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5);
                                tool.StockId = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);
                                tool.ModelName = reader.IsDBNull(7) ? "" : reader.GetString(7);
                                tool.CategoryName = reader.IsDBNull(8) ? "" : reader.GetString(8);
                                tool.StockName = reader.IsDBNull(9) ? "" : reader.GetString(9);
                                tool.AgeYears = CalculateAgeYears(tool.PurchaseDate);
                                tool.DiscountPercent = CalculateDiscountByAge(tool.PurchaseDate);
                                tool.Discount = tool.DiscountPercent > 0 ? $"{tool.DiscountPercent}%" : "-";
                                toolsList.Add(tool);
                            }
                        }
                    }
                }
                ApplyFilter();
                txtStatus.Text = $"Загружено записей: {toolsList.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Применение фильтрации
        private void ApplyFilter()
        {
            if (filteredToolsList == null) return;

            filteredToolsList.Clear();

            string filterName = txtFilterName?.Text?.Trim().ToLower() ?? "";

            string filterStatus = "Все";
            if (cmbFilterStatus?.SelectedItem is ComboBoxItem statusItem)
                filterStatus = statusItem.Content.ToString();

            string filterCondition = "Все";
            if (cmbFilterCondition?.SelectedItem is ComboBoxItem conditionItem)
                filterCondition = conditionItem.Content.ToString();

            int? filterStockId = null;
            if (cmbFilterStock?.SelectedItem is ComboBoxItem stockItem && stockItem.Tag != null)
                filterStockId = (int)stockItem.Tag;

            var filtered = toolsList.Where(tool =>
                (string.IsNullOrEmpty(filterName) || tool.ModelName.ToLower().Contains(filterName)) &&
                (filterStatus == "Все" || tool.Status == filterStatus) &&
                (filterCondition == "Все" || tool.ConditionStatus == filterCondition) &&
                (!filterStockId.HasValue || tool.StockId == filterStockId)
            ).ToList();

            foreach (var tool in filtered)
            {
                filteredToolsList.Add(tool);
            }

            txtStatus.Text = $"Показано: {filteredToolsList.Count} из {toolsList.Count}";
        }

        private void Filter_Changed(object sender, EventArgs e)
        {
            ApplyFilter();
        }

        private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            if (txtFilterName != null) txtFilterName.Text = "";
            if (cmbFilterStatus != null) cmbFilterStatus.SelectedIndex = 0;
            if (cmbFilterCondition != null) cmbFilterCondition.SelectedIndex = 0;
            if (cmbFilterStock != null) cmbFilterStock.SelectedIndex = 0;
        }

        private void ClearForm()
        {
            if (cmbModel != null) cmbModel.SelectedIndex = -1;
            if (txtInventoryNumber != null) txtInventoryNumber.Clear();
            if (cmbStatus != null) cmbStatus.SelectedIndex = 0;
            if (cmbCondition != null) cmbCondition.SelectedIndex = 1;
            if (txtPurchasePrice != null) txtPurchasePrice.Clear();
            if (dpPurchaseDate != null) dpPurchaseDate.SelectedDate = DateTime.Now;
            if (cmbStock != null)
            {
                cmbStock.SelectedIndex = -1;
                cmbStock.IsEnabled = true;
            }
            selectedId = -1;
            UpdateAgeDiscount();
            txtStatus.Text = "Форма очищена. Готов к добавлению.";
        }

        private void UpdateAgeDiscount()
        {
            if (txtAgeDiscount == null) return;

            decimal discount = CalculateDiscountByAge(dpPurchaseDate?.SelectedDate);
            txtAgeDiscount.Text = discount > 0 ? $"{discount}%" : "0%";
            txtAgeDiscount.Foreground = discount > 0 ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Gray;
        }

        private void dpPurchaseDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateAgeDiscount();
        }

        private bool ValidateForm()
        {
            if (cmbModel.SelectedItem == null)
            {
                MessageBox.Show("Выберите модель инструмента!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                cmbModel.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtInventoryNumber.Text))
            {
                MessageBox.Show("Сгенерируйте инвентарный номер!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                btnGenerateInvNum.Focus();
                return false;
            }

            if (cmbStatus.SelectedItem == null)
            {
                MessageBox.Show("Выберите статус инструмента!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (cmbCondition.SelectedItem == null)
            {
                MessageBox.Show("Выберите состояние инструмента!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (dpPurchaseDate.SelectedDate == null)
            {
                MessageBox.Show("Укажите дату покупки инструмента!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                dpPurchaseDate.Focus();
                return false;
            }

            if (dpPurchaseDate.SelectedDate > DateTime.Now)
            {
                MessageBox.Show("Дата покупки не может быть в будущем!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                dpPurchaseDate.Focus();
                return false;
            }

            if (dpPurchaseDate.SelectedDate < new DateTime(2000, 1, 1))
            {
                if (MessageBox.Show("Дата покупки указана ранее 2000 года. Это правильно?",
                    "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    dpPurchaseDate.Focus();
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(txtPurchasePrice.Text))
            {
                decimal price;
                if (!decimal.TryParse(txtPurchasePrice.Text, out price) || price < 0)
                {
                    MessageBox.Show("Цена покупки должна быть положительным числом!",
                        "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtPurchasePrice.Focus();
                    return false;
                }
            }

            return true;
        }

        private void CmbStatus_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbStatus?.SelectedItem != null && cmbCondition != null)
            {
                string selectedStatus = ((ComboBoxItem)cmbStatus.SelectedItem).Content.ToString();

                if (selectedStatus == "списан" || selectedStatus == "утерян")
                {
                    cmbCondition.IsEnabled = false;
                    foreach (ComboBoxItem item in cmbCondition.Items)
                    {
                        if (item.Content.ToString() == selectedStatus)
                        {
                            cmbCondition.SelectedItem = item;
                            break;
                        }
                    }
                    txtStatus.Text = $"ВНИМАНИЕ: У {selectedStatus} инструментов состояние изменить нельзя!";
                }
                else
                {
                    cmbCondition.IsEnabled = true;
                }

                SetStockComboBoxState(selectedStatus);
            }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateForm()) return;

            ModelInfo selectedModel = (ModelInfo)cmbModel.SelectedItem;
            int? stockId = cmbStock.SelectedItem != null ? ((StockInfo)cmbStock.SelectedItem).Id : (int?)null;
            string selectedStatus = ((ComboBoxItem)cmbStatus.SelectedItem).Content.ToString();

            // Если статус "утерян" или "списан", склад не нужен
            if ((selectedStatus == "утерян" || selectedStatus == "списан") && stockId.HasValue)
            {
                if (MessageBox.Show($"Инструмент со статусом \"{selectedStatus}\" не должен быть привязан к складу.\n" +
                    "Оставить склад пустым?", "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    stockId = null;
                    cmbStock.SelectedIndex = -1;
                }
            }

            if (stockId.HasValue && selectedStatus != "утерян" && selectedStatus != "списан")
            {
                var (current, max) = GetStockCapacity(stockId.Value);
                if (current >= max)
                {
                    MessageBox.Show($"Склад переполнен! На этом складе максимум {max} инструментов, а уже {current}.\n" +
                        $"Невозможно добавить новый инструмент на этот склад.",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    cmbStock.Focus();
                    return;
                }

                int remaining = max - current;
                if (remaining <= 2)
                {
                    if (MessageBox.Show($"На складе осталось всего {remaining} мест из {max}.\n" +
                        "Вы уверены, что хотите добавить инструмент на этот склад?",
                        "Предупреждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    {
                        cmbStock.Focus();
                        return;
                    }
                }
            }

            string query = @"
                INSERT INTO public.tools (model_id, inventory_number, status, condition_status, purchase_price, purchase_date, stock_id)
                VALUES (@modelId, @invNumber, @status, @condition, @price, @purchaseDate, @stockId)";

            try
            {
                using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@modelId", selectedModel.Id);
                        cmd.Parameters.AddWithValue("@invNumber", txtInventoryNumber.Text);
                        cmd.Parameters.AddWithValue("@status", selectedStatus);
                        cmd.Parameters.AddWithValue("@condition", ((ComboBoxItem)cmbCondition.SelectedItem).Content.ToString());

                        if (string.IsNullOrEmpty(txtPurchasePrice.Text))
                            cmd.Parameters.AddWithValue("@price", DBNull.Value);
                        else
                            cmd.Parameters.AddWithValue("@price", decimal.Parse(txtPurchasePrice.Text));

                        cmd.Parameters.AddWithValue("@purchaseDate", dpPurchaseDate.SelectedDate.Value);
                        cmd.Parameters.AddWithValue("@stockId", stockId.HasValue ? (object)stockId.Value : DBNull.Value);

                        cmd.ExecuteNonQuery();
                    }
                }

                MessageBox.Show("Инструмент успешно добавлен!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                LoadTools();
                LoadStocks();
                ClearForm();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка добавления: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnMassAdd_Click(object sender, RoutedEventArgs e)
        {
            MassAddWithDetails dialog = new MassAddWithDetails();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                LoadTools();
                LoadStocks();
                ClearForm();
                txtStatus.Text = "Массовое добавление завершено!";
            }
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (selectedId == -1)
            {
                MessageBox.Show("Выберите запись для редактирования!", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ValidateForm()) return;

            string currentStatus = ((ComboBoxItem)cmbStatus.SelectedItem).Content.ToString();

            if (currentStatus == "списан" || currentStatus == "утерян")
            {
                MessageBox.Show($"Невозможно редактировать {currentStatus} инструмент!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!CanChangeStock(selectedId, currentStatus))
            {
                MessageBox.Show($"Невозможно изменить склад инструмента, который находится в статусе \"{currentStatus}\"!\n" +
                    "Склад можно изменить только для инструментов со статусом \"доступен\".",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                cmbStock.Focus();
                return;
            }

            ModelInfo selectedModel = (ModelInfo)cmbModel.SelectedItem;
            int? newStockId = cmbStock.SelectedItem != null ? ((StockInfo)cmbStock.SelectedItem).Id : (int?)null;
            int? oldStockId = null;

            string getOldStockQuery = "SELECT stock_id FROM public.tools WHERE id = @id";
            try
            {
                using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (NpgsqlCommand cmd = new NpgsqlCommand(getOldStockQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", selectedId);
                        var result = cmd.ExecuteScalar();
                        oldStockId = result != DBNull.Value ? Convert.ToInt32(result) : (int?)null;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка получения текущего склада: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (newStockId.HasValue && newStockId != oldStockId)
            {
                var (current, max) = GetStockCapacity(newStockId.Value, selectedId);
                if (current >= max)
                {
                    MessageBox.Show($"Склад переполнен! На этом складе максимум {max} инструментов, а уже {current}.\n" +
                        $"Невозможно переместить инструмент на этот склад.",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    cmbStock.Focus();
                    return;
                }

                int remaining = max - current;
                if (remaining <= 2)
                {
                    if (MessageBox.Show($"На складе осталось всего {remaining} мест из {max}.\n" +
                        "Вы уверены, что хотите переместить инструмент на этот склад?",
                        "Предупреждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    {
                        cmbStock.Focus();
                        return;
                    }
                }
            }

            if (MessageBox.Show("Вы уверены, что хотите сохранить изменения?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            string query = @"
                UPDATE public.tools 
                SET model_id = @modelId,
                    inventory_number = @invNumber,
                    status = @status,
                    condition_status = @condition,
                    purchase_price = @price,
                    purchase_date = @purchaseDate,
                    stock_id = @stockId
                WHERE id = @id";

            try
            {
                using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", selectedId);
                        cmd.Parameters.AddWithValue("@modelId", selectedModel.Id);
                        cmd.Parameters.AddWithValue("@invNumber", txtInventoryNumber.Text);
                        cmd.Parameters.AddWithValue("@status", ((ComboBoxItem)cmbStatus.SelectedItem).Content.ToString());
                        cmd.Parameters.AddWithValue("@condition", ((ComboBoxItem)cmbCondition.SelectedItem).Content.ToString());

                        if (string.IsNullOrEmpty(txtPurchasePrice.Text))
                            cmd.Parameters.AddWithValue("@price", DBNull.Value);
                        else
                            cmd.Parameters.AddWithValue("@price", decimal.Parse(txtPurchasePrice.Text));

                        cmd.Parameters.AddWithValue("@purchaseDate", dpPurchaseDate.SelectedDate.Value);
                        cmd.Parameters.AddWithValue("@stockId", newStockId.HasValue ? (object)newStockId.Value : DBNull.Value);

                        cmd.ExecuteNonQuery();
                    }
                }

                MessageBox.Show("Данные успешно обновлены!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                LoadTools();
                LoadStocks();
                ClearForm();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обновления: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
        }

        private void BtnGenerateInvNum_Click(object sender, RoutedEventArgs e)
        {
            if (cmbModel.SelectedItem == null)
            {
                MessageBox.Show("Сначала выберите модель!", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            txtInventoryNumber.Text = GenerateInventoryNumber();
        }

        private void DgTools_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgTools.SelectedItem is ToolDisplay selected && cmbModel != null && cmbStatus != null && cmbCondition != null)
            {
                selectedId = selected.Id;
                txtInventoryNumber.Text = selected.InventoryNumber;

                foreach (ModelInfo model in modelsList)
                {
                    if (model.Name == selected.ModelName)
                    {
                        cmbModel.SelectedItem = model;
                        break;
                    }
                }

                for (int i = 0; i < cmbStatus.Items.Count; i++)
                {
                    ComboBoxItem item = (ComboBoxItem)cmbStatus.Items[i];
                    if (item.Content.ToString() == selected.Status)
                    {
                        cmbStatus.SelectedIndex = i;
                        break;
                    }
                }

                for (int i = 0; i < cmbCondition.Items.Count; i++)
                {
                    ComboBoxItem item = (ComboBoxItem)cmbCondition.Items[i];
                    if (item.Content.ToString() == selected.ConditionStatus)
                    {
                        cmbCondition.SelectedIndex = i;
                        break;
                    }
                }

                SetStockComboBoxState(selected.Status);

                if (selected.StockId.HasValue)
                {
                    foreach (StockInfo stock in stocksList)
                    {
                        if (stock.Id == selected.StockId.Value)
                        {
                            cmbStock.SelectedItem = stock;
                            break;
                        }
                    }
                }
                else
                {
                    cmbStock.SelectedIndex = -1;
                }

                txtPurchasePrice.Text = selected.PurchasePrice.HasValue ? selected.PurchasePrice.Value.ToString("F2") : "";
                if (dpPurchaseDate != null)
                    dpPurchaseDate.SelectedDate = selected.PurchaseDate;
                txtStatus.Text = $"Выбрана запись: {selected.InventoryNumber}";
                UpdateAgeDiscount();
            }
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9,.]");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void BtnModels_Click(object sender, RoutedEventArgs e)
        {
            AddModel modelsWindow = new AddModel();
            modelsWindow.Owner = this;
            modelsWindow.ShowDialog();
            LoadModels();
            LoadTools();
            txtStatus.Text = "Список моделей обновлен";
        }
    }
}