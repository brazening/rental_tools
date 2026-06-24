using Npgsql;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Rental
{
    public partial class SupplyItemDialog : Window
    {
        private string connString;
        private List<int> existingToolModelIds;
        private SupplyItemView editingItem;
        private bool isEditMode = false;
        private ObservableCollection<ToolModel> toolModelsList;
        private ObservableCollection<Stock> stocksList;
        private int? currentSupplyId = null; // Для редактирования

        public SupplyItemView SelectedItem { get; private set; }

        public class ToolModel
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public decimal RentalPrice { get; set; }
            public int AvailableQuantity { get; set; }
        }

        public class Stock
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int MaxQuantity { get; set; }
            public int CurrentQuantity { get; set; }
            public string DisplayName => $"{Name} ({CurrentQuantity}/{MaxQuantity}, свободно: {MaxQuantity - CurrentQuantity})";
        }

        public SupplyItemDialog(string connectionString, int supplyId, List<int> existingIds, SupplyItemView item = null)
        {
            InitializeComponent();

            this.connString = connectionString;
            this.existingToolModelIds = existingIds ?? new List<int>();
            this.editingItem = item;
            this.isEditMode = (item != null);
            this.currentSupplyId = supplyId > 0 ? supplyId : (int?)null;

            toolModelsList = new ObservableCollection<ToolModel>();
            stocksList = new ObservableCollection<Stock>();

            dgToolModels.ItemsSource = toolModelsList;

            LoadStocks();

            if (isEditMode)
            {
                Title = "Редактирование позиции поставки";
                LoadToolModelsForEdit();
            }
            else
            {
                Title = "Добавление позиции поставки - выбор модели";
                LoadToolModels();
            }
        }

        /// <summary>
        /// Проверка, можно ли добавить указанное количество на склад
        /// </summary>
        private bool CanAddToStock(int stockId, int additionalQuantity, int? excludeSupplyItemId = null)
        {
            string query = @"
                SELECT COUNT(*) 
                FROM public.tools 
                WHERE stock_id = @stockId 
                AND status NOT IN ('списан', 'утерян')";

            try
            {
                using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@stockId", stockId);
                        int currentCount = Convert.ToInt32(cmd.ExecuteScalar());

                        string maxQuery = "SELECT max_quantity FROM public.stock WHERE id = @stockId";
                        using (NpgsqlCommand maxCmd = new NpgsqlCommand(maxQuery, conn))
                        {
                            maxCmd.Parameters.AddWithValue("@stockId", stockId);
                            int maxQuantity = Convert.ToInt32(maxCmd.ExecuteScalar());

                            int newCount = currentCount + additionalQuantity;

                            if (newCount > maxQuantity)
                            {
                                MessageBox.Show($"Невозможно добавить инструменты на склад!\n\n" +
                                    $"Склад: \"{GetStockName(stockId)}\"\n" +
                                    $"Текущее количество активных инструментов: {currentCount}\n" +
                                    $"Максимальная вместимость: {maxQuantity}\n" +
                                    $"Попытка добавить: {additionalQuantity} шт.\n" +
                                    $"Будет занято: {newCount} из {maxQuantity}\n" +
                                    $"Не хватает мест: {newCount - maxQuantity}\n\n" +
                                    $"Пожалуйста, уменьшите количество или выберите другой склад!",
                                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return false;
                            }

                            int remaining = maxQuantity - newCount;
                            if (remaining <= 2 && additionalQuantity > 0)
                            {
                                var result = MessageBox.Show($"ВНИМАНИЕ: На складе \"{GetStockName(stockId)}\" после добавления останется всего {remaining} свободных мест из {maxQuantity}.\n\n" +
                                    $"Добавляется: {additionalQuantity} шт.\n" +
                                    $"Текущий запас: {currentCount} шт.\n" +
                                    $"Будет всего: {newCount} шт.\n\n" +
                                    $"Продолжить?",
                                    "Предупреждение", MessageBoxButton.YesNo, MessageBoxImage.Question);

                                return result == MessageBoxResult.Yes;
                            }

                            return true;
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
        /// Получить текущую заполненность склада
        /// </summary>
        private int GetCurrentStockQuantity(int stockId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"
                        SELECT COUNT(*) 
                        FROM public.tools 
                        WHERE stock_id = @stockId 
                        AND status NOT IN ('списан', 'утерян')";
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@stockId", stockId);
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Обновить отображение заполненности складов
        /// </summary>
        private void UpdateStocksDisplay()
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

                    var cmd = new NpgsqlCommand(query, conn);
                    var dt = new DataTable();
                    dt.Load(cmd.ExecuteReader());

                    var tempStocks = new ObservableCollection<Stock>();
                    foreach (DataRow row in dt.Rows)
                    {
                        tempStocks.Add(new Stock
                        {
                            Id = row.Field<int>("id"),
                            Name = row.Field<string>("name"),
                            MaxQuantity = row.Field<int>("max_quantity"),
                            CurrentQuantity = Convert.ToInt32(row.Field<long>("current_quantity"))
                        });
                    }

                    // Сохраняем текущее выбранное значение
                    int? selectedStockId = cboStock.SelectedValue as int?;

                    stocksList.Clear();
                    foreach (var stock in tempStocks)
                    {
                        stocksList.Add(stock);
                    }

                    cboStock.ItemsSource = stocksList;
                    cboStock.DisplayMemberPath = "DisplayName";
                    cboStock.SelectedValuePath = "Id";

                    // Восстанавливаем выбор
                    if (selectedStockId.HasValue && stocksList.Any(s => s.Id == selectedStockId.Value))
                    {
                        cboStock.SelectedValue = selectedStockId.Value;
                    }
                    else if (stocksList.Count > 0)
                    {
                        cboStock.SelectedIndex = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обновления складов: {ex.Message}");
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

                    var cmd = new NpgsqlCommand(query, conn);
                    var dt = new DataTable();
                    dt.Load(cmd.ExecuteReader());

                    stocksList.Clear();
                    foreach (DataRow row in dt.Rows)
                    {
                        stocksList.Add(new Stock
                        {
                            Id = row.Field<int>("id"),
                            Name = row.Field<string>("name"),
                            MaxQuantity = row.Field<int>("max_quantity"),
                            CurrentQuantity = Convert.ToInt32(row.Field<long>("current_quantity"))
                        });
                    }

                    cboStock.ItemsSource = stocksList;
                    cboStock.DisplayMemberPath = "DisplayName";
                    cboStock.SelectedValuePath = "Id";

                    if (stocksList.Count > 0 && !isEditMode)
                    {
                        cboStock.SelectedIndex = 0;
                    }
                    else if (isEditMode && editingItem.StockId > 0)
                    {
                        var stock = stocksList.FirstOrDefault(s => s.Id == editingItem.StockId);
                        if (stock != null)
                        {
                            cboStock.SelectedValue = stock.Id;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки складов: {ex.Message}");
            }
        }

        private void LoadToolModels()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"
                        SELECT 
                            tm.id, 
                            tm.name, 
                            tm.rental_price,
                            COUNT(t.id) as available_quantity
                        FROM public.tool_models tm
                        LEFT JOIN public.tools t ON t.model_id = tm.id
                            AND t.status NOT IN ('списан', 'утерян', 'в аренде', 'в ремонте')
                        GROUP BY tm.id, tm.name, tm.rental_price
                        ORDER BY tm.name";

                    var cmd = new NpgsqlCommand(query, conn);
                    var dt = new DataTable();
                    dt.Load(cmd.ExecuteReader());

                    toolModelsList.Clear();
                    foreach (DataRow row in dt.Rows)
                    {
                        int id = row.Field<int>("id");
                        if (!existingToolModelIds.Contains(id))
                        {
                            toolModelsList.Add(new ToolModel
                            {
                                Id = id,
                                Name = row.Field<string>("name"),
                                RentalPrice = row.Field<decimal>("rental_price"),
                                AvailableQuantity = row.IsNull("available_quantity") ? 0 : Convert.ToInt32(row.Field<long>("available_quantity"))
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки моделей: {ex.Message}");
            }
        }

        private void LoadToolModelsForEdit()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"
                        SELECT 
                            tm.id, 
                            tm.name, 
                            tm.rental_price,
                            COUNT(t.id) as available_quantity
                        FROM public.tool_models tm
                        LEFT JOIN public.tools t ON t.model_id = tm.id
                            AND t.status NOT IN ('списан', 'утерян', 'в аренде', 'в ремонте')
                        WHERE tm.id = @id
                        GROUP BY tm.id, tm.name, tm.rental_price";

                    var cmd = new NpgsqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@id", editingItem.ToolModelId);
                    var dt = new DataTable();
                    dt.Load(cmd.ExecuteReader());

                    if (dt.Rows.Count > 0)
                    {
                        var row = dt.Rows[0];
                        var selected = new ToolModel
                        {
                            Id = row.Field<int>("id"),
                            Name = row.Field<string>("name"),
                            RentalPrice = row.Field<decimal>("rental_price"),
                            AvailableQuantity = row.IsNull("available_quantity") ? 0 : Convert.ToInt32(row.Field<long>("available_quantity"))
                        };

                        txtSelectedModel.Text = selected.Name;
                        txtQuantity.Text = editingItem.Quantity.ToString();
                        txtPurchasePrice.Text = editingItem.PurchasePrice.ToString("N2");
                        txtAvailableQuantity.Text = selected.AvailableQuantity.ToString();

                        CalculateTotalPrice();
                        btnSave.IsEnabled = true;

                        var tempList = new ObservableCollection<ToolModel>();
                        tempList.Add(selected);
                        dgToolModels.ItemsSource = tempList;
                    }
                }
                dgToolModels.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки модели: {ex.Message}");
            }
        }

        private void DgToolModels_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgToolModels.SelectedItem is ToolModel selected && !isEditMode)
            {
                txtSelectedModel.Text = selected.Name;
                txtAvailableQuantity.Text = selected.AvailableQuantity.ToString();
                txtQuantity.Focus();
                btnSave.IsEnabled = true;
            }
        }

        private void TxtQuantity_TextChanged(object sender, TextChangedEventArgs e)
        {
            CalculateTotalPrice();
            // Проверяем количество при изменении
            ValidateQuantity();
        }

        private void TxtPurchasePrice_TextChanged(object sender, TextChangedEventArgs e)
        {
            CalculateTotalPrice();
        }

        private void CalculateTotalPrice()
        {
            if (int.TryParse(txtQuantity.Text, out int qty) && decimal.TryParse(txtPurchasePrice.Text, out decimal price))
            {
                txtTotalPrice.Text = (qty * price).ToString("N2");
            }
            else
            {
                txtTotalPrice.Text = "0.00";
            }
        }

        private void ValidateQuantity()
        {
            if (cboStock.SelectedValue != null && int.TryParse(txtQuantity.Text, out int quantity) && quantity > 0)
            {
                int stockId = (int)cboStock.SelectedValue;
                int currentCount = GetCurrentStockQuantity(stockId);

                var stock = stocksList.FirstOrDefault(s => s.Id == stockId);
                if (stock != null && currentCount + quantity > stock.MaxQuantity)
                {
                    txtQuantity.Background = System.Windows.Media.Brushes.LightPink;
                    btnSave.IsEnabled = false;
                }
                else
                {
                    txtQuantity.Background = System.Windows.Media.Brushes.White;
                    // Также проверяем, что выбрана модель
                    if (isEditMode || dgToolModels.SelectedItem != null)
                    {
                        btnSave.IsEnabled = true;
                    }
                }
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            ToolModel selected = null;

            if (isEditMode)
            {
                selected = (dgToolModels.ItemsSource as ObservableCollection<ToolModel>)?.FirstOrDefault();
            }
            else
            {
                selected = dgToolModels.SelectedItem as ToolModel;
            }

            if (selected == null && !isEditMode)
            {
                MessageBox.Show("Выберите модель инструмента");
                return;
            }

            if (cboStock.SelectedValue == null)
            {
                MessageBox.Show("Выберите склад");
                return;
            }

            if (!int.TryParse(txtQuantity.Text, out int quantity) || quantity <= 0)
            {
                MessageBox.Show("Количество должно быть целым положительным числом");
                return;
            }

            if (!decimal.TryParse(txtPurchasePrice.Text, out decimal price) || price <= 0)
            {
                MessageBox.Show("Цена закупки должна быть положительным числом");
                return;
            }

            int stockId = (int)cboStock.SelectedValue;

            // ПРОВЕРКА ЗАПОЛНЕННОСТИ СКЛАДА
            if (!CanAddToStock(stockId, quantity))
            {
                return;
            }

            decimal totalPrice = quantity * price;

            SelectedItem = new SupplyItemView
            {
                Id = isEditMode ? editingItem.Id : 0,
                ToolModelId = selected.Id,
                ToolModelName = selected.Name,
                Quantity = quantity,
                PurchasePrice = price,
                TotalPrice = totalPrice,
                StockId = stockId,
                StockName = (cboStock.SelectedItem as Stock)?.Name ?? ""
            };

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}