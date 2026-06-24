using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Npgsql;

namespace Rental
{
    /// <summary>
    /// Класс для представления города доставки
    /// </summary>
    public class DeliveryCity
    {
        public int Id { get; set; }
        public string CityName { get; set; }
        public decimal DeliveryCost { get; set; }
    }

    public partial class AddDeliveryCity : Window
    {
        private ObservableCollection<DeliveryCity> _deliveryCities;
        private int _selectedId = -1;
        private string _connectionString;

        public AddDeliveryCity()
        {
            InitializeComponent();

            // Получение строки подключения из App.xaml.cs
            var app = (App)Application.Current;
            _connectionString = app.connString;

            _deliveryCities = new ObservableCollection<DeliveryCity>();
            dgDeliveryCities.ItemsSource = _deliveryCities;

            LoadDeliveryCities();
        }

        /// <summary>
        /// Загрузка данных из таблицы delivery_cities
        /// </summary>
        private void LoadDeliveryCities()
        {
            try
            {
                _deliveryCities.Clear();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = "SELECT id, city_name, delivery_cost FROM public.delivery_cities ORDER BY id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            _deliveryCities.Add(new DeliveryCity
                            {
                                Id = reader.GetInt32(0),
                                CityName = reader.GetString(1),
                                DeliveryCost = reader.GetDecimal(2)
                            });
                        }
                    }
                }

                txtStatus.Text = $"Готово - Загружено записей: {_deliveryCities.Count}";
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
            txtCityName.Text = string.Empty;
            txtDeliveryCost.Text = string.Empty;
            _selectedId = -1;
            txtStatus.Text = "Готово - Форма очищена";
        }

        /// <summary>
        /// Валидация названия города
        /// </summary>
        private bool ValidateCityName(string cityName)
        {
            if (string.IsNullOrWhiteSpace(cityName))
            {
                MessageBox.Show("Поле 'Город' обязательно для заполнения!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (cityName.Length > 100)
            {
                MessageBox.Show("Название города не может превышать 100 символов!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Валидация стоимости доставки
        /// </summary>
        private bool ValidateDeliveryCost(string costStr, out decimal cost)
        {
            cost = 0;

            if (string.IsNullOrWhiteSpace(costStr))
            {
                MessageBox.Show("Поле 'Стоимость доставки' обязательно для заполнения!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // Замена запятой на точку для корректного парсинга
            costStr = costStr.Replace(',', '.');

            if (!decimal.TryParse(costStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out cost))
            {
                MessageBox.Show("Поле 'Стоимость доставки' должно содержать корректное число!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (cost < 0)
            {
                MessageBox.Show("Стоимость доставки не может быть отрицательной!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Проверка на существование города (для INSERT и UPDATE)
        /// </summary>
        private bool IsCityExists(string cityName, int excludeId = -1)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = "SELECT COUNT(*) FROM public.delivery_cities WHERE city_name = @cityName";

                    if (excludeId > 0)
                    {
                        query += " AND id != @excludeId";
                    }

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@cityName", cityName);
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
                MessageBox.Show($"Ошибка проверки города: {ex.Message}", "Ошибка",
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
                if (!ValidateCityName(txtCityName.Text))
                    return;

                if (!ValidateDeliveryCost(txtDeliveryCost.Text, out decimal cost))
                    return;

                string cityName = txtCityName.Text.Trim();

                // Проверка на дубликат
                if (IsCityExists(cityName))
                {
                    MessageBox.Show($"Город '{cityName}' уже существует в базе данных!", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // INSERT в БД
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"INSERT INTO public.delivery_cities (city_name, delivery_cost) 
                                     VALUES (@cityName, @deliveryCost)";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@cityName", cityName);
                        cmd.Parameters.AddWithValue("@deliveryCost", cost);
                        cmd.ExecuteNonQuery();
                    }
                }

                // Обновление DataGrid
                LoadDeliveryCities();
                ClearForm();

                MessageBox.Show("Город успешно добавлен!", "Успех",
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
                if (!ValidateCityName(txtCityName.Text))
                    return;

                if (!ValidateDeliveryCost(txtDeliveryCost.Text, out decimal cost))
                    return;

                string cityName = txtCityName.Text.Trim();

                // Проверка на дубликат (исключая текущий ID)
                if (IsCityExists(cityName, _selectedId))
                {
                    MessageBox.Show($"Город '{cityName}' уже существует в базе данных!", "Ошибка",
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
                    string query = @"UPDATE public.delivery_cities 
                                     SET city_name = @cityName, delivery_cost = @deliveryCost 
                                     WHERE id = @id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@cityName", cityName);
                        cmd.Parameters.AddWithValue("@deliveryCost", cost);
                        cmd.Parameters.AddWithValue("@id", _selectedId);
                        cmd.ExecuteNonQuery();
                    }
                }

                // Обновление DataGrid
                LoadDeliveryCities();
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
        private void dgDeliveryCities_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgDeliveryCities.SelectedItem is DeliveryCity selected)
            {
                _selectedId = selected.Id;
                txtCityName.Text = selected.CityName;
                txtDeliveryCost.Text = selected.DeliveryCost.ToString();
                txtStatus.Text = $"Выбрана запись: {selected.CityName}";
            }
        }

        /// <summary>
        /// Валидация ввода для числовых полей
        /// </summary>
        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9.,]");
            e.Handled = regex.IsMatch(e.Text);
        }
    }
}