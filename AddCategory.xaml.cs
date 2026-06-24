using System;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Npgsql;

namespace Rental
{
    /// <summary>
    /// Класс категории
    /// </summary>
    public class Category
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Окно управления таблицей categories
    /// </summary>
    public partial class AddCategory : Window
    {
        private ObservableCollection<Category> _categories = new ObservableCollection<Category>();
        private int selectedId = -1;
        private string _connectionString;

        public AddCategory()
        {
            InitializeComponent();

            // Получение строки подключения из App.xaml.cs
            if (Application.Current is App app)
            {
                _connectionString = app.connString;
            }
            else
            {
                // Резервный вариант
                _connectionString = "Host=localhost;Port=5432;Database=rental_tools;Username=postgres;Password=your_password";
            }

            dgCategories.ItemsSource = _categories;
            LoadCategories();
        }

        /// <summary>
        /// Загрузка данных из таблицы categories
        /// </summary>
        private void LoadCategories()
        {
            try
            {
                _categories.Clear();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = "SELECT id, name FROM public.categories ORDER BY id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            _categories.Add(new Category
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.GetString(1)
                            });
                        }
                    }
                }

                UpdateStatus($"Загружено записей: {_categories.Count}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Ошибка загрузки данных");
            }
        }

        /// <summary>
        /// Очистка формы
        /// </summary>
        private void ClearForm()
        {
            txtName.Clear();
            selectedId = -1;
            // Снимаем выделение в DataGrid
            dgCategories.UnselectAll();
            UpdateStatus("Форма очищена. Готов к добавлению новой записи.");
        }

        /// <summary>
        /// Валидация поля name
        /// </summary>
        private bool ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Название категории не может быть пустым!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (name.Length > 100)
            {
                MessageBox.Show("Название категории не может превышать 100 символов!", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Проверка на уникальность названия категории
        /// </summary>
        private bool IsNameUnique(string name, int excludeId = -1)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = "SELECT COUNT(*) FROM public.categories WHERE LOWER(name) = LOWER(@name)";
                    if (excludeId > 0)
                    {
                        query += " AND id != @excludeId";
                    }

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@name", name);
                        if (excludeId > 0)
                        {
                            cmd.Parameters.AddWithValue("@excludeId", excludeId);
                        }

                        long count = (long)cmd.ExecuteScalar();
                        return count == 0;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка проверки уникальности: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Добавление новой записи
        /// </summary>
        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string name = txtName.Text.Trim();

                // Валидация
                if (!ValidateName(name)) return;
                if (!IsNameUnique(name))
                {
                    MessageBox.Show("Категория с таким названием уже существует!", "Ошибка валидации",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // INSERT в БД
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = "INSERT INTO public.categories (name) VALUES (@name) RETURNING id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@name", name);
                        int newId = (int)cmd.ExecuteScalar();

                        // Добавление в коллекцию
                        _categories.Add(new Category { Id = newId, Name = name });
                    }
                }

                MessageBox.Show("Категория успешно добавлена!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                ClearForm();
                UpdateStatus($"Добавлена категория: {name}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка добавления: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Ошибка при добавлении");
            }
        }

        /// <summary>
        /// Редактирование записи
        /// </summary>
        private void btnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (selectedId == -1)
            {
                MessageBox.Show("Выберите запись для редактирования!", "Предупреждение",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string name = txtName.Text.Trim();

                // Валидация
                if (!ValidateName(name)) return;
                if (!IsNameUnique(name, selectedId))
                {
                    MessageBox.Show("Категория с таким названием уже существует!", "Ошибка валидации",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Подтверждение
                var result = MessageBox.Show("Вы уверены, что хотите сохранить изменение?",
                    "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;

                // UPDATE в БД
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = "UPDATE public.categories SET name = @name WHERE id = @id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@name", name);
                        cmd.Parameters.AddWithValue("@id", selectedId);
                        cmd.ExecuteNonQuery();

                        // Обновление в коллекции
                        var category = FindCategoryById(selectedId);
                        if (category != null)
                        {
                            category.Name = name;
                        }
                    }
                }

                // Обновляем DataGrid
                dgCategories.Items.Refresh();

                MessageBox.Show("Категория успешно обновлена!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                // Очищаем форму и сбрасываем выделение
                ClearForm();
                UpdateStatus($"Обновлена категория ID={selectedId}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обновления: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Ошибка при обновлении");
            }
        }

        /// <summary>
        /// Очистка формы
        /// </summary>
        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
        }

        /// <summary>
        /// Выбор строки в DataGrid
        /// </summary>
        private void dgCategories_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (dgCategories.SelectedItem is Category selectedCategory)
            {
                selectedId = selectedCategory.Id;
                txtName.Text = selectedCategory.Name;
                UpdateStatus($"Выбрана категория: {selectedCategory.Name}");
            }
        }

        /// <summary>
        /// Валидация ввода для поля name (только буквы, пробелы и дефисы)
        /// </summary>
        private void txtName_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Разрешаем буквы (русские и английские), пробелы, дефисы и цифры (для категорий типа "Инструмент 2.0")
            Regex regex = new Regex(@"[^а-яА-Яa-zA-Z0-9\s\-\.]");
            e.Handled = regex.IsMatch(e.Text);
        }

        /// <summary>
        /// Поиск категории по ID
        /// </summary>
        private Category FindCategoryById(int id)
        {
            foreach (var category in _categories)
            {
                if (category.Id == id)
                    return category;
            }
            return null;
        }

        /// <summary>
        /// Обновление статуса
        /// </summary>
        private void UpdateStatus(string message)
        {
            txtStatus.Text = message;
        }
    }
}