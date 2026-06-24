using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Npgsql;

namespace Rental
{
    /// <summary>
    /// Модель для отображения в DataGrid
    /// </summary>
    public class ToolModelDisplay
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int? CategoryId { get; set; }
        public string CategoryName { get; set; }
        public string Description { get; set; }
        public decimal RentalPrice { get; set; }
        public decimal OverdueCoefficient { get; set; }
        public int? ServiceLifeMonths { get; set; }
        public int TotalQuantity { get; set; }
    }

    /// <summary>
    /// Модель категории для ComboBox
    /// </summary>
    public class CategoryItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Модель для отображения количества инструментов по складам
    /// </summary>
    public class StockQuantityDisplay
    {
        public int StockId { get; set; }
        public string StockName { get; set; }
        public int Quantity { get; set; }
    }

    public partial class AddModel : Window
    {
        private readonly string _connectionString;
        private ObservableCollection<ToolModelDisplay> _toolModels;
        private List<CategoryItem> _categories;
        private int? _selectedId = null;

        public AddModel()
        {
            InitializeComponent();

            // Получение строки подключения из App.xaml.cs
            var app = (App)Application.Current;
            _connectionString = app.connString;

            if (string.IsNullOrEmpty(_connectionString))
            {
                MessageBox.Show("Ошибка: строка подключения не найдена!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            _toolModels = new ObservableCollection<ToolModelDisplay>();
            dgToolModels.ItemsSource = _toolModels;

            LoadCategories();
            LoadToolModels();

            // Установка значения по умолчанию для коэффициента
            txtOverdueCoefficient.Text = "1.00";
        }

        /// <summary>
        /// Загрузка категорий в ComboBox
        /// </summary>
        private void LoadCategories()
        {
            try
            {
                _categories = new List<CategoryItem>();
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = "SELECT id, name FROM public.categories ORDER BY name";
                    using (var cmd = new NpgsqlCommand(query, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            _categories.Add(new CategoryItem
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.GetString(1)
                            });
                        }
                    }
                }

                cmbCategory.ItemsSource = _categories;
                cmbCategory.SelectedIndex = -1;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки категорий: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Загрузка данных tool_models из БД с подсчетом активных инструментов
        /// </summary>
        private void LoadToolModels()
        {
            try
            {
                _toolModels.Clear();
                UpdateStatus("Загрузка данных...");

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"
                        SELECT 
                            tm.id, 
                            tm.name, 
                            tm.category_id, 
                            tm.description, 
                            tm.rental_price, 
                            tm.overdue_coefficient, 
                            tm.service_life_months,
                            c.name as category_name,
                            COUNT(t.id) as total_quantity
                        FROM public.tool_models tm
                        LEFT JOIN public.categories c ON tm.category_id = c.id
                        LEFT JOIN public.tools t ON t.model_id = tm.id AND (t.status IS NULL OR t.status != 'списан')
                        GROUP BY 
                            tm.id, tm.name, tm.category_id, tm.description, 
                            tm.rental_price, tm.overdue_coefficient, tm.service_life_months, c.name
                        ORDER BY tm.id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var displayModel = new ToolModelDisplay
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.GetString(1),
                                CategoryId = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2),
                                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                                RentalPrice = reader.GetDecimal(4),
                                OverdueCoefficient = reader.GetDecimal(5),
                                ServiceLifeMonths = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6),
                                CategoryName = reader.IsDBNull(7) ? null : reader.GetString(7),
                                TotalQuantity = reader.IsDBNull(8) ? 0 : reader.GetInt32(8)
                            };
                            _toolModels.Add(displayModel);
                        }
                    }
                }

                UpdateStatus($"Загружено записей: {_toolModels.Count}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Ошибка загрузки данных");
            }
        }

        /// <summary>
        /// Обновление строки статуса
        /// </summary>
        private void UpdateStatus(string message)
        {
            txtStatus.Text = message;
        }

        /// <summary>
        /// Очистка формы ввода
        /// </summary>
        private void ClearForm()
        {
            txtName.Text = string.Empty;
            txtDescription.Text = string.Empty;
            txtRentalPrice.Text = string.Empty;
            txtOverdueCoefficient.Text = "1.00";
            txtServiceLifeMonths.Text = string.Empty;
            txtTotalQuantity.Text = string.Empty;
            cmbCategory.SelectedIndex = -1;
            _selectedId = null;
            UpdateStatus("Форма очищена");
        }

        /// <summary>
        /// Валидация обязательных полей
        /// </summary>
        private bool ValidateForm(out string errorMessage)
        {
            errorMessage = string.Empty;

            // Проверка названия модели
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                errorMessage = "Поле 'Название модели' обязательно для заполнения";
                return false;
            }
            if (txtName.Text.Length > 150)
            {
                errorMessage = "Название модели не может превышать 150 символов";
                return false;
            }

            // Проверка цены аренды
            if (string.IsNullOrWhiteSpace(txtRentalPrice.Text))
            {
                errorMessage = "Поле 'Цена аренды' обязательно для заполнения";
                return false;
            }

            // Парсинг цены с учетом точки и запятой
            if (!TryParseDecimal(txtRentalPrice.Text, out decimal rentalPrice) || rentalPrice < 0)
            {
                errorMessage = "Цена аренды должна быть положительным числом";
                return false;
            }

            // Проверка коэффициента просрочки
            if (string.IsNullOrWhiteSpace(txtOverdueCoefficient.Text))
            {
                errorMessage = "Поле 'Коэф. просрочки' обязательно для заполнения";
                return false;
            }

            // Парсинг коэффициента с учетом точки и запятой
            if (!TryParseDecimal(txtOverdueCoefficient.Text, out decimal overdueCoeff) || overdueCoeff < 0)
            {
                errorMessage = "Коэффициент просрочки должен быть положительным числом";
                return false;
            }

            // Проверка срока службы (опционально)
            if (!string.IsNullOrWhiteSpace(txtServiceLifeMonths.Text))
            {
                if (!int.TryParse(txtServiceLifeMonths.Text, out int serviceLife) || serviceLife < 0)
                {
                    errorMessage = "Срок службы должен быть целым положительным числом";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Попытка парсинга decimal с поддержкой точки и запятой
        /// </summary>
        private bool TryParseDecimal(string input, out decimal result)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                result = 0;
                return false;
            }

            // Заменяем запятую на точку для парсинга
            string normalized = input.Replace(',', '.');
            return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
        }

        /// <summary>
        /// Добавление новой записи
        /// </summary>
        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateForm(out string errorMessage))
                {
                    MessageBox.Show(errorMessage, "Ошибка валидации",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Получаем значения с корректным парсингом
                decimal rentalPrice = ParseDecimal(txtRentalPrice.Text);
                decimal overdueCoeff = ParseDecimal(txtOverdueCoefficient.Text);

                int? serviceLifeMonths = null;
                if (!string.IsNullOrWhiteSpace(txtServiceLifeMonths.Text))
                {
                    serviceLifeMonths = int.Parse(txtServiceLifeMonths.Text);
                }

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"
                        INSERT INTO public.tool_models 
                            (name, category_id, description, rental_price, overdue_coefficient, service_life_months)
                        VALUES 
                            (@name, @category_id, @description, @rental_price, @overdue_coefficient, @service_life_months)";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@name", txtName.Text.Trim());
                        cmd.Parameters.AddWithValue("@category_id",
                            cmbCategory.SelectedItem == null ? (object)DBNull.Value : ((CategoryItem)cmbCategory.SelectedItem).Id);
                        cmd.Parameters.AddWithValue("@description",
                            string.IsNullOrWhiteSpace(txtDescription.Text) ? (object)DBNull.Value : txtDescription.Text.Trim());
                        cmd.Parameters.AddWithValue("@rental_price", rentalPrice);
                        cmd.Parameters.AddWithValue("@overdue_coefficient", overdueCoeff);
                        cmd.Parameters.AddWithValue("@service_life_months",
                            serviceLifeMonths.HasValue ? (object)serviceLifeMonths.Value : DBNull.Value);

                        int rowsAffected = cmd.ExecuteNonQuery();
                        if (rowsAffected > 0)
                        {
                            MessageBox.Show("Запись успешно добавлена!", "Успех",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            LoadToolModels();
                            ClearForm();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при добавлении: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Ошибка добавления");
            }
        }

        /// <summary>
        /// Парсинг decimal с заменой запятой на точку
        /// </summary>
        private decimal ParseDecimal(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return 0;
            string normalized = input.Replace(',', '.');
            return decimal.Parse(normalized, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Редактирование выбранной записи
        /// </summary>
        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedId == null)
            {
                MessageBox.Show("Выберите запись для редактирования", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (!ValidateForm(out string errorMessage))
                {
                    MessageBox.Show(errorMessage, "Ошибка валидации",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show("Сохранить изменения?", "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                // Получаем значения с корректным парсингом
                decimal rentalPrice = ParseDecimal(txtRentalPrice.Text);
                decimal overdueCoeff = ParseDecimal(txtOverdueCoefficient.Text);

                int? serviceLifeMonths = null;
                if (!string.IsNullOrWhiteSpace(txtServiceLifeMonths.Text))
                {
                    serviceLifeMonths = int.Parse(txtServiceLifeMonths.Text);
                }

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"
                        UPDATE public.tool_models SET
                            name = @name,
                            category_id = @category_id,
                            description = @description,
                            rental_price = @rental_price,
                            overdue_coefficient = @overdue_coefficient,
                            service_life_months = @service_life_months
                        WHERE id = @id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", _selectedId.Value);
                        cmd.Parameters.AddWithValue("@name", txtName.Text.Trim());
                        cmd.Parameters.AddWithValue("@category_id",
                            cmbCategory.SelectedItem == null ? (object)DBNull.Value : ((CategoryItem)cmbCategory.SelectedItem).Id);
                        cmd.Parameters.AddWithValue("@description",
                            string.IsNullOrWhiteSpace(txtDescription.Text) ? (object)DBNull.Value : txtDescription.Text.Trim());
                        cmd.Parameters.AddWithValue("@rental_price", rentalPrice);
                        cmd.Parameters.AddWithValue("@overdue_coefficient", overdueCoeff);
                        cmd.Parameters.AddWithValue("@service_life_months",
                            serviceLifeMonths.HasValue ? (object)serviceLifeMonths.Value : DBNull.Value);

                        int rowsAffected = cmd.ExecuteNonQuery();
                        if (rowsAffected > 0)
                        {
                            MessageBox.Show("Запись успешно обновлена!", "Успех",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            LoadToolModels();
                            ClearForm();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при редактировании: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Ошибка редактирования");
            }
        }

        /// <summary>
        /// Очистка формы
        /// </summary>
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
        }

        /// <summary>
        /// Обработчик выбора строки в DataGrid
        /// </summary>
        private void DgToolModels_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = dgToolModels.SelectedItem as ToolModelDisplay;
            if (selected == null) return;

            _selectedId = selected.Id;
            txtName.Text = selected.Name;
            txtDescription.Text = selected.Description ?? string.Empty;
            txtRentalPrice.Text = selected.RentalPrice.ToString();
            txtOverdueCoefficient.Text = selected.OverdueCoefficient.ToString(CultureInfo.InvariantCulture);
            txtServiceLifeMonths.Text = selected.ServiceLifeMonths?.ToString() ?? string.Empty;
            txtTotalQuantity.Text = selected.TotalQuantity.ToString();

            if (selected.CategoryId.HasValue)
            {
                cmbCategory.SelectedItem = _categories.FirstOrDefault(c => c.Id == selected.CategoryId.Value);
            }
            else
            {
                cmbCategory.SelectedIndex = -1;
            }

            UpdateStatus($"Выбрана запись: {selected.Name}");
        }

        /// <summary>
        /// Показать окно с распределением инструментов по складам
        /// </summary>
        private void BtnShowStockDetails_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedId == null)
            {
                MessageBox.Show("Выберите модель для просмотра количества по складам", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                List<StockQuantityDisplay> stockQuantities = new List<StockQuantityDisplay>();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"
                        SELECT 
                            s.id,
                            s.name as stock_name,
                            COUNT(t.id) as quantity
                        FROM public.stock s
                        LEFT JOIN public.tools t ON t.stock_id = s.id AND t.model_id = @model_id AND (t.status IS NULL OR t.status != 'списан')
                        GROUP BY s.id, s.name
                        ORDER BY s.name";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@model_id", _selectedId.Value);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                stockQuantities.Add(new StockQuantityDisplay
                                {
                                    StockId = reader.GetInt32(0),
                                    StockName = reader.GetString(1),
                                    Quantity = reader.IsDBNull(2) ? 0 : reader.GetInt32(2)
                                });
                            }
                        }
                    }
                }

                // Создаем и показываем диалоговое окно
                var stockWindow = new Window
                {
                    Title = $"Распределение по складам - {txtName.Text}",
                    Width = 450,
                    Height = 350,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 45)),
                    ResizeMode = ResizeMode.NoResize
                };

                var mainGrid = new Grid { Margin = new Thickness(10) };
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // Заголовок
                var titleText = new TextBlock
                {
                    Text = $"Модель: {txtName.Text}",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                Grid.SetRow(titleText, 0);
                mainGrid.Children.Add(titleText);

                // DataGrid для отображения складов
                var stockGrid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    Background = System.Windows.Media.Brushes.White,
                    BorderBrush = System.Windows.Media.Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    RowHeight = 30,
                    FontSize = 12,
                    IsReadOnly = true,
                    HeadersVisibility = DataGridHeadersVisibility.Column
                };

                stockGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Склад",
                    Binding = new System.Windows.Data.Binding("StockName"),
                    Width = new DataGridLength(200)
                });

                stockGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Количество",
                    Binding = new System.Windows.Data.Binding("Quantity"),
                    Width = new DataGridLength(100)
                });

                stockGrid.ItemsSource = stockQuantities;
                Grid.SetRow(stockGrid, 1);
                mainGrid.Children.Add(stockGrid);

                // Панель для итоговой строки и кнопки
                var bottomPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

                // Итоговая строка
                var totalQuantity = stockQuantities.Sum(sq => sq.Quantity);
                var totalText = new TextBlock
                {
                    Text = $"Итого: {totalQuantity} шт.",
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White,
                    TextAlignment = TextAlignment.Right,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                bottomPanel.Children.Add(totalText);

                // Кнопка закрытия
                var closeButton = new Button
                {
                    Content = "Закрыть",
                    Width = 100,
                    Height = 30,
                    Background = System.Windows.Media.Brushes.LightGray,
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                closeButton.Click += (s, ev) => stockWindow.Close();
                bottomPanel.Children.Add(closeButton);

                Grid.SetRow(bottomPanel, 2);
                mainGrid.Children.Add(bottomPanel);

                stockWindow.Content = mainGrid;
                stockWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке данных по складам: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Валидация ввода - только целые числа
        /// </summary>
        private void Number_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
        }

        /// <summary>
        /// Валидация ввода - числа с десятичной точкой или запятой
        /// </summary>
        private void Decimal_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = sender as TextBox;
            string currentText = textBox?.Text ?? "";
            string newText = currentText.Insert(textBox.SelectionStart, e.Text);
            e.Handled = !Regex.IsMatch(newText, @"^-?\d*[.,]?\d*$");
        }

        /// <summary>
        /// Обработчик потери фокуса для поля коэффициента - нормализует формат
        /// </summary>
        private void TxtOverdueCoefficient_LostFocus(object sender, RoutedEventArgs e)
        {
            if (TryParseDecimal(txtOverdueCoefficient.Text, out decimal value))
            {
                txtOverdueCoefficient.Text = value.ToString(CultureInfo.InvariantCulture);
            }
            else if (!string.IsNullOrWhiteSpace(txtOverdueCoefficient.Text))
            {
                MessageBox.Show("Введите корректное число (например: 1.5 или 1,5)",
                    "Ошибка формата", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtOverdueCoefficient.Text = "1.00";
            }
        }
    }
}