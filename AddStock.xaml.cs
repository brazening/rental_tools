using System;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Npgsql;

namespace Rental
{
    /// <summary>
    /// Класс для представления склада
    /// </summary>
    public class Stock
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int MaxQuantity { get; set; }
        public int ToolsCount { get; set; }  // Количество инструментов на складе
    }

    public partial class AddStock : Window
    {
        private ObservableCollection<Stock> _stocks;
        private int _selectedId = -1;
        private string _connectionString;

        public AddStock()
        {
            InitializeComponent();

            // Получение строки подключения из App.xaml.cs
            var app = (App)Application.Current;
            _connectionString = app.connString;

            _stocks = new ObservableCollection<Stock>();
            dgStock.ItemsSource = _stocks;

            LoadStocks();
        }

        /// <summary>
        /// Загрузка данных из таблицы stock с подсчетом количества инструментов
        /// </summary>
        private void LoadStocks()
        {
            try
            {
                _stocks.Clear();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"
                SELECT s.id, s.name, s.max_quantity, 
                       COUNT(t.id) as tools_count
                FROM public.stock s
                LEFT JOIN public.tools t ON t.stock_id = s.id 
                    AND t.status NOT IN ('списан', 'утерян')  -- Добавлено условие!
                GROUP BY s.id, s.name, s.max_quantity
                ORDER BY s.id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            _stocks.Add(new Stock
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.GetString(1),
                                MaxQuantity = reader.GetInt32(2),
                                ToolsCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3)
                            });
                        }
                    }
                }

                txtStatus.Text = $"Готово - Загружено складов: {_stocks.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Text = "Ошибка загрузки данных";
            }
        }

        /// <summary>
        /// Очистка формы
        /// </summary>
        private void ClearForm()
        {
            txtStockName.Text = string.Empty;
            txtMaxQuantity.Text = string.Empty;
            _selectedId = -1;
            txtStatus.Text = "Готово - Форма очищена";
        }

        /// <summary>
        /// Валидация названия склада
        /// </summary>
        private bool ValidateStockName(string stockName)
        {
            if (string.IsNullOrWhiteSpace(stockName))
            {
                MessageBox.Show("Поле 'Название склада' обязательно для заполнения!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (stockName.Length > 100)
            {
                MessageBox.Show("Название склада не может превышать 100 символов!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Валидация максимальной вместимости
        /// </summary>
        private bool ValidateMaxQuantity(string quantityStr, out int quantity)
        {
            quantity = 0;

            if (string.IsNullOrWhiteSpace(quantityStr))
            {
                MessageBox.Show("Поле 'Максимальная вместимость' обязательно для заполнения!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!int.TryParse(quantityStr, out quantity))
            {
                MessageBox.Show("Поле 'Максимальная вместимость' должно содержать целое число!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (quantity <= 0)
            {
                MessageBox.Show("Максимальная вместимость должна быть больше 0!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Проверка на существование склада (для INSERT и UPDATE)
        /// </summary>
        private bool IsStockExists(string stockName, int excludeId = -1)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = "SELECT COUNT(*) FROM public.stock WHERE name = @stockName";

                    if (excludeId > 0)
                    {
                        query += " AND id != @excludeId";
                    }

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@stockName", stockName);
                        if (excludeId > 0)
                        {
                            cmd.Parameters.AddWithValue("@excludeId", excludeId);
                        }

                        long count = (long)cmd.ExecuteScalar();
                        return count > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка проверки склада: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return true;
            }
        }

        /// <summary>
        /// Кнопка "Добавить"
        /// </summary>
        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Валидация полей
                if (!ValidateStockName(txtStockName.Text))
                    return;

                if (!ValidateMaxQuantity(txtMaxQuantity.Text, out int maxQuantity))
                    return;

                string stockName = txtStockName.Text.Trim();

                // Проверка на дубликат
                if (IsStockExists(stockName))
                {
                    MessageBox.Show($"Склад '{stockName}' уже существует в базе данных!", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // INSERT в БД
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"INSERT INTO public.stock (name, max_quantity) 
                                     VALUES (@stockName, @maxQuantity)";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@stockName", stockName);
                        cmd.Parameters.AddWithValue("@maxQuantity", maxQuantity);
                        cmd.ExecuteNonQuery();
                    }
                }

                // Обновление DataGrid
                LoadStocks();
                ClearForm();

                MessageBox.Show("Склад успешно добавлен!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при добавлении: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Text = "Ошибка при добавлении записи";
            }
        }

        /// <summary>
        /// Кнопка "Редактировать"
        /// </summary>
        private void btnEdit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedId == -1)
                {
                    MessageBox.Show("Выберите запись для редактирования!", "Предупреждение",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Валидация полей
                if (!ValidateStockName(txtStockName.Text))
                    return;

                if (!ValidateMaxQuantity(txtMaxQuantity.Text, out int maxQuantity))
                    return;

                string stockName = txtStockName.Text.Trim();

                // Проверка на дубликат (исключая текущий ID)
                if (IsStockExists(stockName, _selectedId))
                {
                    MessageBox.Show($"Склад '{stockName}' уже существует в базе данных!", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Подтверждение
                var result = MessageBox.Show("Вы уверены, что хотите сохранить изменения?", "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                // UPDATE в БД
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"UPDATE public.stock 
                                     SET name = @stockName, max_quantity = @maxQuantity 
                                     WHERE id = @id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@stockName", stockName);
                        cmd.Parameters.AddWithValue("@maxQuantity", maxQuantity);
                        cmd.Parameters.AddWithValue("@id", _selectedId);
                        cmd.ExecuteNonQuery();
                    }
                }

                // Обновление DataGrid
                LoadStocks();
                ClearForm();

                MessageBox.Show("Запись успешно обновлена!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при редактировании: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Text = "Ошибка при редактировании записи";
            }
        }

        /// <summary>
        /// Кнопка "Очистить"
        /// </summary>
        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
        }

        /// <summary>
        /// Обработчик выбора строки в DataGrid
        /// </summary>
        private void dgStock_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgStock.SelectedItem is Stock selected)
            {
                _selectedId = selected.Id;
                txtStockName.Text = selected.Name;
                txtMaxQuantity.Text = selected.MaxQuantity.ToString();
                txtStatus.Text = $"Выбран склад: {selected.Name} (инструментов: {selected.ToolsCount})";
            }
        }

        /// <summary>
        /// Валидация ввода для числовых полей
        /// </summary>
        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]");
            e.Handled = regex.IsMatch(e.Text);
        }
    }
}