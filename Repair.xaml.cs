using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Npgsql;

namespace Rental
{
    public partial class Repair : Window
    {
        private readonly string _connectionString;
        private ObservableCollection<RepairItem> _repairs;
        private int _selectedId = -1;
        private string _currentStatus = "";

        public Repair()
        {
            InitializeComponent();
            // Получаем строку подключения из App
            var app = (App)Application.Current;
            _connectionString = app.connString; _repairs = new ObservableCollection<RepairItem>();
            dgRepairs.ItemsSource = _repairs;
            dpStartDate.SelectedDate = DateTime.Today;

            LoadComboBoxes();
            LoadRepairs();

            // Блокируем кнопки изменения статуса пока не выбран ремонт
            SetStatusButtonsState(false);
        }

        // Модель данных для инструментов в ComboBox
        public class ToolItem
        {
            public int Id { get; set; }
            public string DisplayName { get; set; }
            public string Status { get; set; }
        }

        // Модель данных для сотрудников в ComboBox
        public class EmployeeItem
        {
            public int Id { get; set; }
            public string DisplayName { get; set; }
        }

        // Класс RepairItem для отображения в DataGrid
        public class RepairItem : INotifyPropertyChanged
        {
            private int _id;
            private int _toolId;
            private string _toolInventoryNumber;
            private int _employeeId;
            private string _employeeFIO;
            private string _issueDescription;
            private string _status;
            private decimal? _cost;
            private DateTime _startDate;
            private DateTime? _endDate;

            public int Id
            {
                get => _id;
                set { _id = value; OnPropertyChanged(nameof(Id)); }
            }

            public int ToolId
            {
                get => _toolId;
                set { _toolId = value; OnPropertyChanged(nameof(ToolId)); }
            }

            public string ToolInventoryNumber
            {
                get => _toolInventoryNumber;
                set { _toolInventoryNumber = value; OnPropertyChanged(nameof(ToolInventoryNumber)); }
            }

            public int EmployeeId
            {
                get => _employeeId;
                set { _employeeId = value; OnPropertyChanged(nameof(EmployeeId)); }
            }

            public string EmployeeFIO
            {
                get => _employeeFIO;
                set { _employeeFIO = value; OnPropertyChanged(nameof(EmployeeFIO)); }
            }

            public string IssueDescription
            {
                get => _issueDescription;
                set { _issueDescription = value; OnPropertyChanged(nameof(IssueDescription)); }
            }

            public string Status
            {
                get => _status;
                set { _status = value; OnPropertyChanged(nameof(Status)); }
            }

            public decimal? Cost
            {
                get => _cost;
                set { _cost = value; OnPropertyChanged(nameof(Cost)); }
            }

            public DateTime StartDate
            {
                get => _startDate;
                set { _startDate = value; OnPropertyChanged(nameof(StartDate)); }
            }

            public DateTime? EndDate
            {
                get => _endDate;
                set { _endDate = value; OnPropertyChanged(nameof(EndDate)); }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        // Управление доступностью кнопок статуса
        private void SetStatusButtonsState(bool isEnabled)
        {
            btnSetInProgress.IsEnabled = isEnabled;
            btnSetWaiting.IsEnabled = isEnabled;
            btnSetCompleted.IsEnabled = isEnabled;
            btnSetCancelled.IsEnabled = isEnabled;
        }

        // Блокировка/разблокировка редактируемых полей
        private void SetEditableFieldsState(bool isEnabled)
        {
            cmbTool.IsEnabled = isEnabled;
            cmbEmployee.IsEnabled = isEnabled;
            txtIssueDescription.IsEnabled = isEnabled;
            txtCost.IsEnabled = isEnabled;
            dpStartDate.IsEnabled = isEnabled;
            dpEndDate.IsEnabled = isEnabled;
            btnAdd.IsEnabled = isEnabled;
            btnEdit.IsEnabled = isEnabled;
        }

        // Обновление UI в зависимости от статуса
        private void UpdateUIByStatus(string status)
        {
            if (status == "завершен" || status == "отменен")
            {
                // Если ремонт завершен или отменен - запрещаем редактирование
                SetEditableFieldsState(false);
                btnEdit.IsEnabled = false;
                btnAdd.IsEnabled = false;
                //txtStatus.Background = System.Windows.Media.Brushes.LightGray;
            }
            else
            {
                // Активный ремонт - можно редактировать
                SetEditableFieldsState(true);
                //txtStatus.Background = System.Windows.Media.Brushes.LightYellow;
            }

            // Настройка видимости кнопок статуса
            if (status == "завершен")
            {
                btnSetCompleted.IsEnabled = false;
                btnSetCancelled.IsEnabled = false;
            }
            else if (status == "отменен")
            {
                btnSetCompleted.IsEnabled = false;
                btnSetCancelled.IsEnabled = false;
            }
            else
            {
                btnSetCompleted.IsEnabled = true;
                btnSetCancelled.IsEnabled = true;
            }
        }

        // Окрашивание строк в зависимости от статуса
        //private void dgRepairs_LoadingRow(object sender, DataGridRowEventArgs e)
        //{
        //    RepairItem item = e.Row.DataContext as RepairItem;
        //    if (item != null)
        //    {
        //        switch (item.Status)
        //        {
        //            case "завершен":
        //                e.Row.Background = System.Windows.Media.Brushes.LightGreen;
        //                break;
        //            case "в процессе":
        //                e.Row.Background = System.Windows.Media.Brushes.LightYellow;
        //                break;
        //            case "ожидает запчастей":
        //                e.Row.Background = System.Windows.Media.Brushes.LightCoral;
        //                break;
        //            case "отменен":
        //                e.Row.Background = System.Windows.Media.Brushes.LightGray;
        //                break;
        //            default:
        //                e.Row.Background = System.Windows.Media.Brushes.White;
        //                break;
        //        }
        //    }
        //}

        private void LoadComboBoxes()
        {
            try
            {
                // Загрузка инструментов (только те, что не списаны)
                var tools = new ObservableCollection<ToolItem>();
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    var cmd = new NpgsqlCommand(@"
                        SELECT t.id, t.inventory_number, t.status 
                        FROM public.tools t
                        WHERE (t.status != 'списан' OR t.status IS NULL)
                        ORDER BY t.inventory_number", conn);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tools.Add(new ToolItem
                            {
                                Id = reader.GetInt32(0),
                                DisplayName = reader.GetString(1),
                                Status = reader.IsDBNull(2) ? "доступен" : reader.GetString(2)
                            });
                        }
                    }
                }
                cmbTool.ItemsSource = tools;

                // Загрузка сотрудников
                var employees = new ObservableCollection<EmployeeItem>();
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    var cmd = new NpgsqlCommand(@"
                        SELECT id, fio 
                        FROM public.employees
                        ORDER BY fio", conn);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            employees.Add(new EmployeeItem
                            {
                                Id = reader.GetInt32(0),
                                DisplayName = reader.GetString(1)
                            });
                        }
                    }
                }
                cmbEmployee.ItemsSource = employees;

                statusText.Text = $"Справочники загружены. Инструментов: {tools.Count}, Сотрудников: {employees.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки справочников: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                statusText.Text = "Ошибка загрузки справочников";
            }
        }

        private void LoadRepairs()
        {
            try
            {
                _repairs.Clear();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    var cmd = new NpgsqlCommand(@"
                        SELECT 
                            r.id, 
                            r.tool_id, 
                            t.inventory_number as ToolInventoryNumber, 
                            r.employee_id, 
                            e.fio as EmployeeFIO, 
                            r.issue_description, 
                            r.status, 
                            r.cost, 
                            r.start_date, 
                            r.end_date
                        FROM public.repairs r
                        LEFT JOIN public.tools t ON r.tool_id = t.id
                        LEFT JOIN public.employees e ON r.employee_id = e.id
                        ORDER BY r.id DESC", conn);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            _repairs.Add(new RepairItem
                            {
                                Id = reader.GetInt32(0),
                                ToolId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                                ToolInventoryNumber = reader.IsDBNull(2) ? "-" : reader.GetString(2),
                                EmployeeId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                                EmployeeFIO = reader.IsDBNull(4) ? "-" : reader.GetString(4),
                                IssueDescription = reader.IsDBNull(5) ? "" : reader.GetString(5),
                                Status = reader.IsDBNull(6) ? "в процессе" : reader.GetString(6),
                                Cost = reader.IsDBNull(7) ? null : (decimal?)reader.GetDecimal(7),
                                StartDate = reader.GetDateTime(8),
                                EndDate = reader.IsDBNull(9) ? null : (DateTime?)reader.GetDateTime(9)
                            });
                        }
                    }
                }

                statusText.Text = $"Загружено записей: {_repairs.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                statusText.Text = "Ошибка загрузки данных";
            }
        }

        private void cmbTool_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Можно добавить дополнительную логику при выборе инструмента
        }

        private void ClearForm()
        {
            cmbTool.SelectedItem = null;
            cmbEmployee.SelectedItem = null;
            txtIssueDescription.Clear();
            txtCost.Clear();
            dpStartDate.SelectedDate = DateTime.Today;
            dpEndDate.SelectedDate = null;
            txtStatus.Text = "";
            _selectedId = -1;
            _currentStatus = "";
            btnEdit.IsEnabled = false;

            // Разблокируем все поля для новой записи
            SetEditableFieldsState(true);
            SetStatusButtonsState(false);
            txtStatus.Background = System.Windows.Media.Brushes.White;

            statusText.Text = "Форма очищена. Выберите запись для редактирования или добавьте новую.";
        }

        private bool ValidateToolSelected()
        {
            if (cmbTool.SelectedItem == null)
            {
                MessageBox.Show("Пожалуйста, выберите инструмент.", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                cmbTool.Focus();
                return false;
            }
            return true;
        }

        private bool ValidateEmployeeSelected()
        {
            if (cmbEmployee.SelectedItem == null)
            {
                MessageBox.Show("Пожалуйста, выберите сотрудника.", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                cmbEmployee.Focus();
                return false;
            }
            return true;
        }

        private bool ValidateIssueDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                MessageBox.Show("Пожалуйста, укажите описание проблемы.", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtIssueDescription.Focus();
                return false;
            }
            if (description.Length > 1000)
            {
                MessageBox.Show("Описание проблемы не должно превышать 1000 символов.", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtIssueDescription.Focus();
                return false;
            }
            return true;
        }

        private bool ValidateCost(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            if (decimal.TryParse(value, NumberStyles.Any,
                CultureInfo.InvariantCulture, out decimal result))
            {
                if (result < 0 || result > 999999.99m)
                {
                    MessageBox.Show("Стоимость ремонта должна быть в диапазоне от 0 до 999999.99.",
                        "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtCost.Focus();
                    return false;
                }
                return true;
            }
            else
            {
                MessageBox.Show("Неверный формат стоимости. Используйте цифры и точку как разделитель.",
                    "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtCost.Focus();
                return false;
            }
        }

        private bool ValidateStartDate()
        {
            if (dpStartDate.SelectedDate == null)
            {
                MessageBox.Show("Пожалуйста, выберите дату начала ремонта.", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                dpStartDate.Focus();
                return false;
            }
            return true;
        }

        private bool ValidateEndDate()
        {
            if (dpEndDate.SelectedDate.HasValue && dpStartDate.SelectedDate.HasValue)
            {
                if (dpEndDate.SelectedDate.Value < dpStartDate.SelectedDate.Value)
                {
                    MessageBox.Show("Дата окончания не может быть раньше даты начала.",
                        "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                    dpEndDate.Focus();
                    return false;
                }
            }
            return true;
        }

        // Общий метод для изменения статуса
        private void ChangeStatus(string newStatus, DateTime? endDate = null)
        {
            if (_selectedId == -1)
            {
                MessageBox.Show("Пожалуйста, выберите ремонт для изменения статуса.", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var result = MessageBox.Show($"Вы уверены, что хотите изменить статус на '{newStatus}'?",
                    "Подтверждение изменения статуса", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                var toolItem = (ToolItem)cmbTool.SelectedItem;

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    // Обновляем статус инструмента в зависимости от нового статуса ремонта
                    if (newStatus == "завершен")
                    {
                        var updateToolCmd = new NpgsqlCommand(@"
                            UPDATE public.tools 
                            SET status = 'доступен', condition_status = 'хорошее'
                            WHERE id = @tool_id", conn);
                        updateToolCmd.Parameters.AddWithValue("@tool_id", toolItem.Id);
                        updateToolCmd.ExecuteNonQuery();
                    }
                    else if (newStatus == "отменен")
                    {
                        var updateToolCmd = new NpgsqlCommand(@"
                            UPDATE public.tools 
                            SET status = 'доступен', condition_status = 'хорошее'
                            WHERE id = @tool_id", conn);
                        updateToolCmd.Parameters.AddWithValue("@tool_id", toolItem.Id);
                        updateToolCmd.ExecuteNonQuery();
                    }

                    // Обновляем статус ремонта
                    string updateQuery = @"UPDATE public.repairs SET status = @status";
                    if (endDate.HasValue)
                    {
                        updateQuery += ", end_date = @end_date";
                    }
                    updateQuery += " WHERE id = @id";

                    var cmd = new NpgsqlCommand(updateQuery, conn);
                    cmd.Parameters.AddWithValue("@status", newStatus);
                    if (endDate.HasValue)
                    {
                        cmd.Parameters.AddWithValue("@end_date", endDate.Value);
                    }
                    cmd.Parameters.AddWithValue("@id", _selectedId);

                    cmd.ExecuteNonQuery();
                }

                LoadRepairs();

                // Обновляем текущую запись в форме
                var updatedRepair = _repairs.FirstOrDefault(r => r.Id == _selectedId);
                if (updatedRepair != null)
                {
                    txtStatus.Text = updatedRepair.Status;
                    _currentStatus = updatedRepair.Status;
                    UpdateUIByStatus(_currentStatus);
                }

                statusText.Text = $"Статус ремонта #{_selectedId} изменен на '{newStatus}'";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при изменении статуса: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                statusText.Text = "Ошибка при изменении статуса";
            }
        }

        // Кнопки управления статусом
        private void btnSetInProgress_Click(object sender, RoutedEventArgs e)
        {
            ChangeStatus("в процессе");
        }

        private void btnSetWaiting_Click(object sender, RoutedEventArgs e)
        {
            ChangeStatus("ожидает запчастей");
        }

        private void btnSetCompleted_Click(object sender, RoutedEventArgs e)
        {
            ChangeStatus("завершен", DateTime.Today);
        }

        private void btnSetCancelled_Click(object sender, RoutedEventArgs e)
        {
            ChangeStatus("отменен", DateTime.Today);
        }

        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateToolSelected()) return;
                if (!ValidateEmployeeSelected()) return;
                if (!ValidateIssueDescription(txtIssueDescription.Text)) return;
                if (!ValidateCost(txtCost.Text)) return;
                if (!ValidateStartDate()) return;
                if (!ValidateEndDate()) return;

                var toolItem = (ToolItem)cmbTool.SelectedItem;
                var employeeItem = (EmployeeItem)cmbEmployee.SelectedItem;

                decimal? cost = null;
                if (!string.IsNullOrWhiteSpace(txtCost.Text))
                {
                    cost = decimal.Parse(txtCost.Text, CultureInfo.InvariantCulture);
                }

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    // Обновляем статус инструмента на "в ремонте"
                    var updateToolCmd = new NpgsqlCommand(@"
                        UPDATE public.tools 
                        SET status = 'в ремонте', condition_status = 'ремонт'
                        WHERE id = @tool_id", conn);
                    updateToolCmd.Parameters.AddWithValue("@tool_id", toolItem.Id);
                    updateToolCmd.ExecuteNonQuery();

                    // Добавляем запись о ремонте
                    var cmd = new NpgsqlCommand(@"
                        INSERT INTO public.repairs (tool_id, employee_id, issue_description, status, cost, start_date, end_date)
                        VALUES (@tool_id, @employee_id, @issue_description, 'в процессе', @cost, @start_date, @end_date)", conn);

                    cmd.Parameters.AddWithValue("@tool_id", toolItem.Id);
                    cmd.Parameters.AddWithValue("@employee_id", employeeItem.Id);
                    cmd.Parameters.AddWithValue("@issue_description", txtIssueDescription.Text);
                    cmd.Parameters.AddWithValue("@cost", cost.HasValue ? (object)cost.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@start_date", dpStartDate.SelectedDate.Value);
                    cmd.Parameters.AddWithValue("@end_date", dpEndDate.SelectedDate.HasValue ? (object)dpEndDate.SelectedDate.Value : DBNull.Value);

                    cmd.ExecuteNonQuery();
                }

                LoadRepairs();
                ClearForm();
                statusText.Text = "Запись успешно добавлена, инструмент помечен как 'в ремонте'";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при добавлении записи: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                statusText.Text = "Ошибка при добавлении";
            }
        }

        private void btnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedId == -1)
            {
                MessageBox.Show("Пожалуйста, выберите запись для редактирования.", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Проверяем, можно ли редактировать (не завершен и не отменен)
            if (_currentStatus == "завершен" || _currentStatus == "отменен")
            {
                MessageBox.Show("Нельзя редактировать завершенный или отмененный ремонт.", "Предупреждение",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (!ValidateToolSelected()) return;
                if (!ValidateEmployeeSelected()) return;
                if (!ValidateIssueDescription(txtIssueDescription.Text)) return;
                if (!ValidateCost(txtCost.Text)) return;
                if (!ValidateStartDate()) return;
                if (!ValidateEndDate()) return;

                var result = MessageBox.Show("Вы уверены, что хотите сохранить изменения?",
                    "Подтверждение редактирования", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                var toolItem = (ToolItem)cmbTool.SelectedItem;
                var employeeItem = (EmployeeItem)cmbEmployee.SelectedItem;

                decimal? cost = null;
                if (!string.IsNullOrWhiteSpace(txtCost.Text))
                {
                    cost = decimal.Parse(txtCost.Text, CultureInfo.InvariantCulture);
                }

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    var cmd = new NpgsqlCommand(@"
                        UPDATE public.repairs
                        SET tool_id=@tool_id, employee_id=@employee_id, issue_description=@issue_description, 
                            cost=@cost, start_date=@start_date, end_date=@end_date
                        WHERE id=@id", conn);

                    cmd.Parameters.AddWithValue("@tool_id", toolItem.Id);
                    cmd.Parameters.AddWithValue("@employee_id", employeeItem.Id);
                    cmd.Parameters.AddWithValue("@issue_description", txtIssueDescription.Text);
                    cmd.Parameters.AddWithValue("@cost", cost.HasValue ? (object)cost.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@start_date", dpStartDate.SelectedDate.Value);
                    cmd.Parameters.AddWithValue("@end_date", dpEndDate.SelectedDate.HasValue ? (object)dpEndDate.SelectedDate.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@id", _selectedId);

                    cmd.ExecuteNonQuery();
                }

                LoadRepairs();
                ClearForm();
                statusText.Text = "Запись успешно обновлена";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при обновлении записи: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                statusText.Text = "Ошибка при обновлении";
            }
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
        }

        private void dgRepairs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgRepairs.SelectedItem is RepairItem selected)
            {
                _selectedId = selected.Id;
                _currentStatus = selected.Status;
                btnEdit.IsEnabled = selected.Status != "завершен" && selected.Status != "отменен";

                // Активируем кнопки изменения статуса
                SetStatusButtonsState(true);

                // Заполнение формы
                var tool = ((ObservableCollection<ToolItem>)cmbTool.ItemsSource)
                    .FirstOrDefault(t => t.Id == selected.ToolId);
                cmbTool.SelectedItem = tool;

                var employee = ((ObservableCollection<EmployeeItem>)cmbEmployee.ItemsSource)
                    .FirstOrDefault(emp => emp.Id == selected.EmployeeId);
                cmbEmployee.SelectedItem = employee;

                txtIssueDescription.Text = selected.IssueDescription;
                txtCost.Text = selected.Cost?.ToString(CultureInfo.InvariantCulture) ?? "";
                dpStartDate.SelectedDate = selected.StartDate;
                dpEndDate.SelectedDate = selected.EndDate;
                txtStatus.Text = selected.Status;

                // Обновляем UI в зависимости от статуса
                UpdateUIByStatus(selected.Status);

                statusText.Text = $"Редактирование записи #{selected.Id}";
            }
            else
            {
                btnEdit.IsEnabled = false;
                SetStatusButtonsState(false);
            }
        }

        // Валидация ввода для числовых полей
        private void TextBox_PreviewTextInput_Numeric(object sender, TextCompositionEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            string newText = textBox.Text.Insert(textBox.CaretIndex, e.Text);

            bool isValid = true;
            int dotCount = 0;

            foreach (char c in newText)
            {
                if (c == '.')
                {
                    dotCount++;
                    if (dotCount > 1)
                    {
                        isValid = false;
                        break;
                    }
                }
                else if (!char.IsDigit(c))
                {
                    isValid = false;
                    break;
                }
            }

            e.Handled = !isValid;
        }
    }
}