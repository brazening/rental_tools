using System;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Npgsql;

namespace Rental
{
    public partial class MassAddWithDetails : Window
    {
        private string connString;
        private ObservableCollection<ModelInfo> modelsList;
        private ObservableCollection<StockInfo> stocksList;

        public class ModelInfo
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        public class StockInfo
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int MaxQuantity { get; set; }
        }

        public MassAddWithDetails()
        {
            InitializeComponent();

            var app = (App)Application.Current;
            connString = app.connString;

            modelsList = new ObservableCollection<ModelInfo>();
            stocksList = new ObservableCollection<StockInfo>();

            cmbModel.ItemsSource = modelsList;
            cmbStock.ItemsSource = stocksList;

            LoadModels();
            LoadStocks();
        }

        private void LoadModels()
        {
            modelsList.Clear();
            string query = @"
                SELECT tm.id, tm.name
                FROM public.tool_models tm
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
                                modelsList.Add(model);
                            }
                        }
                    }
                }

                if (modelsList.Count > 0)
                    cmbModel.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки моделей: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadStocks()
        {
            stocksList.Clear();
            string query = @"
                SELECT id, name, max_quantity
                FROM public.stock
                ORDER BY name";

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
                                stocksList.Add(stock);
                            }
                        }
                    }
                }

                if (stocksList.Count > 0)
                    cmbStock.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки складов: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GenerateInventoryNumber(string modelName, int currentCount)
        {
            // Берем первые 3 буквы названия модели (только буквы)
            string prefix = "";
            int count = 0;

            foreach (char c in modelName)
            {
                if (char.IsLetter(c))
                {
                    prefix += char.ToUpper(c);
                    count++;
                    if (count >= 3)
                        break;
                }
            }

            // Если меньше 3 букв, добавляем 'X'
            while (prefix.Length < 3)
            {
                prefix += "X";
            }

            // Формат: ПРЕ-001, ПРЕ-002 и т.д.
            return $"{prefix}-{currentCount:D3}";
        }

        private int GetCurrentToolsCount(int modelId)
        {
            string query = "SELECT COUNT(*) FROM public.tools WHERE model_id = @modelId";
            int count = 0;

            try
            {
                using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@modelId", modelId);
                        count = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка подсчета: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return count;
        }

        private void GeneratePreview()
        {
            if (cmbModel.SelectedItem == null)
            {
                MessageBox.Show("Выберите модель!", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (cmbStock.SelectedItem == null)
            {
                MessageBox.Show("Выберите склад!", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtQuantity.Text, out int quantity) || quantity <= 0 || quantity > 100)
            {
                MessageBox.Show("Введите корректное количество (1-100)!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ModelInfo selectedModel = (ModelInfo)cmbModel.SelectedItem;
            int currentCount = GetCurrentToolsCount(selectedModel.Id);

            lstPreview.Items.Clear();

            for (int i = 1; i <= quantity; i++)
            {
                string invNumber = GenerateInventoryNumber(selectedModel.Name, currentCount + i);
                lstPreview.Items.Add($"{i}. {invNumber}");
            }

            txtStatus.Text = $"Сгенерировано {quantity} инвентарных номеров для модели \"{selectedModel.Name}\"";
        }

        private void PerformMassAdd()
        {
            if (cmbModel.SelectedItem == null)
            {
                MessageBox.Show("Выберите модель инструмента!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (cmbStock.SelectedItem == null)
            {
                MessageBox.Show("Выберите склад!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtQuantity.Text, out int quantity) || quantity <= 0 || quantity > 100)
            {
                MessageBox.Show("Введите корректное количество (1-100)!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ModelInfo selectedModel = (ModelInfo)cmbModel.SelectedItem;
            StockInfo selectedStock = (StockInfo)cmbStock.SelectedItem;

            string status = ((ComboBoxItem)cmbStatus.SelectedItem).Content.ToString();
            string condition = ((ComboBoxItem)cmbCondition.SelectedItem).Content.ToString();

            decimal? purchasePrice = null;
            if (!string.IsNullOrWhiteSpace(txtPurchasePrice.Text))
            {
                decimal price;
                if (decimal.TryParse(txtPurchasePrice.Text, out price) && price >= 0)
                    purchasePrice = price;
                else
                {
                    MessageBox.Show("Цена покупки должна быть положительным числом!", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            DateTime? purchaseDate = dpPurchaseDate.SelectedDate;
            if (purchaseDate == null)
                purchaseDate = DateTime.Today;

            int successCount = 0;
            string errors = "";
            int currentNumber = GetCurrentToolsCount(selectedModel.Id) + 1;

            for (int i = 1; i <= quantity; i++)
            {
                string invNumber = GenerateInventoryNumber(selectedModel.Name, currentNumber);
                currentNumber++;

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
                            cmd.Parameters.AddWithValue("@invNumber", invNumber);
                            cmd.Parameters.AddWithValue("@status", status);
                            cmd.Parameters.AddWithValue("@condition", condition);
                            cmd.Parameters.AddWithValue("@stockId", selectedStock.Id);
                            cmd.Parameters.AddWithValue("@purchaseDate", purchaseDate.Value);

                            if (purchasePrice.HasValue)
                                cmd.Parameters.AddWithValue("@price", purchasePrice.Value);
                            else
                                cmd.Parameters.AddWithValue("@price", DBNull.Value);

                            cmd.ExecuteNonQuery();
                            successCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors += $"#{i}: {ex.Message}\n";
                }
            }

            string message = $"✅ Успешно добавлено: {successCount} из {quantity} инструментов\n";
            message += $"📦 Склад: {selectedStock.Name}\n";
            message += $"🔧 Модель: {selectedModel.Name}\n";
            message += $"📅 Дата покупки: {purchaseDate:dd.MM.yyyy}\n";

            if (purchasePrice.HasValue)
                message += $"💰 Цена покупки: {purchasePrice:N2} руб.\n";

            if (!string.IsNullOrEmpty(errors))
                message += $"\n❌ Ошибки:\n{errors}";
            else
                message += "\n🎉 Все инструменты успешно добавлены!";

            MessageBox.Show(message, "Результат массового добавления",
                MessageBoxButton.OK, successCount > 0 ? MessageBoxImage.Information : MessageBoxImage.Error);

            if (successCount > 0)
                DialogResult = true;
        }

        private void BtnRefreshModels_Click(object sender, RoutedEventArgs e)
        {
            LoadModels();
            txtStatus.Text = "Список моделей обновлен";
        }

        private void BtnGeneratePreview_Click(object sender, RoutedEventArgs e)
        {
            GeneratePreview();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            PerformMassAdd();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9,.]");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void NumberOnlyValidation(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]");
            e.Handled = regex.IsMatch(e.Text);
        }
    }
}