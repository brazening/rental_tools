using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Npgsql;

namespace Rental
{
    public partial class WriteOff : Window
    {
        private readonly string _connectionString;
        private ObservableCollection<WriteOffItem> _writeOffs;
        private int _selectedId = -1;

        public WriteOff()
        {
            InitializeComponent();
            var app = (App)Application.Current;
            _connectionString = app.connString;
            _writeOffs = new ObservableCollection<WriteOffItem>();
            dgWriteOffs.ItemsSource = _writeOffs;
            dpWriteOffDate.SelectedDate = DateTime.Today;

            LoadComboBoxes();
            LoadWriteOffs();
        }

        // Модель данных для инструментов в ComboBox
        public class ToolItem
        {
            public int Id { get; set; }
            public string DisplayName { get; set; }
            public decimal? PurchasePrice { get; set; }
            public DateTime? PurchaseDate { get; set; }
            public int? ServiceLifeMonths { get; set; }
        }

        // Модель данных для сотрудников в ComboBox
        public class EmployeeItem
        {
            public int Id { get; set; }
            public string DisplayName { get; set; }
        }

        // Класс WriteOffItem для отображения в DataGrid
        public class WriteOffItem : INotifyPropertyChanged
        {
            private int _id;
            private int _toolId;
            private string _toolInventoryNumber;
            private int _employeeId;
            private string _employeeFIO;
            private string _reason;
            private decimal? _residualValue;
            private DateTime _writeOffDate;

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

            public string Reason
            {
                get => _reason;
                set { _reason = value; OnPropertyChanged(nameof(Reason)); }
            }

            public decimal? ResidualValue
            {
                get => _residualValue;
                set { _residualValue = value; OnPropertyChanged(nameof(ResidualValue)); }
            }

            public DateTime WriteOffDate
            {
                get => _writeOffDate;
                set { _writeOffDate = value; OnPropertyChanged(nameof(WriteOffDate)); }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        /// <summary>
        /// Расчёт остаточной стоимости методом "по времени" (линейная амортизация)
        /// Формула: ОС = ПС - ((ПС - ЛС) / С * n)
        /// где:
        /// ПС - первоначальная стоимость (purchase_price)
        /// ЛС - ликвидационная стоимость (по умолчанию 0)
        /// С - срок службы в месяцах (service_life_months)
        /// n - количество месяцев с даты покупки до даты списания
        /// </summary>
        private decimal? CalculateResidualValue(ToolItem tool, DateTime writeOffDate)
        {
            // Проверка наличия цены покупки
            if (!tool.PurchasePrice.HasValue || tool.PurchasePrice.Value <= 0)
            {
                txtCalcInfo.Text = "❌ Нет цены покупки";
                return null;
            }

            // Проверка наличия даты покупки
            if (!tool.PurchaseDate.HasValue)
            {
                txtCalcInfo.Text = "❌ Нет даты покупки";
                return null;
            }

            // Если дата списания раньше даты покупки
            if (writeOffDate < tool.PurchaseDate.Value)
            {
                txtCalcInfo.Text = "⚠️ Дата списания раньше даты покупки";
                return tool.PurchasePrice.Value;
            }

            // Срок службы (месяцев) - если не указан, используем 24 месяца по умолчанию
            int serviceLifeMonths = tool.ServiceLifeMonths ?? 24;
            if (serviceLifeMonths <= 0) serviceLifeMonths = 24;

            // Ликвидационная стоимость (можно позже сделать настраиваемой)
            decimal liquidationValue = 0;

            // Количество полных месяцев с даты покупки до даты списания
            int monthsInService = ((writeOffDate.Year - tool.PurchaseDate.Value.Year) * 12)
                                + (writeOffDate.Month - tool.PurchaseDate.Value.Month);

            // Корректировка: если число месяца списания меньше числа месяца покупки,
            // считаем, что полный месяц ещё не прошёл
            if (writeOffDate.Day < tool.PurchaseDate.Value.Day && monthsInService > 0)
            {
                monthsInService--;
            }

            if (monthsInService < 0) monthsInService = 0;

            // Ежемесячное удешевление
            decimal monthlyDepreciation = (tool.PurchasePrice.Value - liquidationValue) / serviceLifeMonths;

            // Остаточная стоимость
            decimal residualValue = tool.PurchasePrice.Value - (monthlyDepreciation * monthsInService);

            // Остаточная стоимость не может быть ниже ликвидационной
            if (residualValue < liquidationValue)
                residualValue = liquidationValue;

            // Округляем до 2 знаков
            residualValue = Math.Round(residualValue, 2);

            // Выводим информацию о расчёте
            if (monthsInService >= serviceLifeMonths)
            {
                txtCalcInfo.Text = $"📐 Срок службы истёк ({monthsInService} из {serviceLifeMonths} мес.) → 0 руб.";
            }
            else
            {
                txtCalcInfo.Text = $"📐 ПС: {tool.PurchasePrice.Value:F2} руб. | " +
                                   $"Срок службы: {serviceLifeMonths} мес. | " +
                                   $"Прошло: {monthsInService} мес. | " +
                                   $"Ежемесячное удешевление: {monthlyDepreciation:F2} руб. | " +
                                   $"ОС = {residualValue:F2} руб.";
            }

            return residualValue;
        }

        private void LoadComboBoxes()
        {
            try
            {
                // Загрузка доступных инструментов (статус не "списан")
                var tools = new ObservableCollection<ToolItem>();
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    var cmd = new NpgsqlCommand(@"
                        SELECT 
                            t.id, 
                            t.inventory_number, 
                            t.purchase_price,
                            t.purchase_date,
                            tm.service_life_months
                        FROM public.tools t
                        LEFT JOIN public.tool_models tm ON t.model_id = tm.id
                        WHERE (t.status != 'списан' OR t.status IS NULL)
                          AND (t.condition_status != 'списан' OR t.condition_status IS NULL)
                        ORDER BY t.inventory_number", conn);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tools.Add(new ToolItem
                            {
                                Id = reader.GetInt32(0),
                                DisplayName = reader.GetString(1),
                                PurchasePrice = reader.IsDBNull(2) ? null : (decimal?)reader.GetDecimal(2),
                                PurchaseDate = reader.IsDBNull(3) ? null : (DateTime?)reader.GetDateTime(3),
                                ServiceLifeMonths = reader.IsDBNull(4) ? null : (int?)reader.GetInt32(4)
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

        private void LoadWriteOffs()
        {
            try
            {
                _writeOffs.Clear();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    var cmd = new NpgsqlCommand(@"
                        SELECT 
                            w.id, 
                            w.tool_id, 
                            t.inventory_number as ToolInventoryNumber, 
                            w.employee_id, 
                            e.fio as EmployeeFIO, 
                            w.reason, 
                            w.residual_value, 
                            w.write_off_date
                        FROM public.write_offs w
                        LEFT JOIN public.tools t ON w.tool_id = t.id
                        LEFT JOIN public.employees e ON w.employee_id = e.id
                        ORDER BY w.id DESC", conn);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            _writeOffs.Add(new WriteOffItem
                            {
                                Id = reader.GetInt32(0),
                                ToolId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                                ToolInventoryNumber = reader.IsDBNull(2) ? "-" : reader.GetString(2),
                                EmployeeId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                                EmployeeFIO = reader.IsDBNull(4) ? "-" : reader.GetString(4),
                                Reason = reader.IsDBNull(5) ? "" : reader.GetString(5),
                                ResidualValue = reader.IsDBNull(6) ? null : (decimal?)reader.GetDecimal(6),
                                WriteOffDate = reader.GetDateTime(7)
                            });
                        }
                    }
                }

                statusText.Text = $"Загружено записей: {_writeOffs.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                statusText.Text = "Ошибка загрузки данных";
            }
        }

        // Автоматический расчёт остаточной стоимости при выборе инструмента
        private void cmbTool_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbTool.SelectedItem is ToolItem selectedTool && dpWriteOffDate.SelectedDate.HasValue)
            {
                decimal? residualValue = CalculateResidualValue(selectedTool, dpWriteOffDate.SelectedDate.Value);

                if (residualValue.HasValue)
                {
                    txtResidualValue.Text = residualValue.Value.ToString("F2",
                        System.Globalization.CultureInfo.InvariantCulture);
                    statusText.Text = $"Выбран инструмент: {selectedTool.DisplayName}, остаточная стоимость: {residualValue.Value:F2} руб.";
                }
                else
                {
                    txtResidualValue.Clear();
                    statusText.Text = $"Выбран инструмент: {selectedTool.DisplayName}, недостаточно данных для расчёта";
                }
            }
            else if (cmbTool.SelectedItem is ToolItem selectedToolOnly)
            {
                txtResidualValue.Clear();
                statusText.Text = $"Выбран инструмент: {selectedToolOnly.DisplayName}, выберите дату списания для расчёта";
            }
            else
            {
                txtResidualValue.Clear();
                txtCalcInfo.Text = "";
            }
        }

        // Автоматический пересчёт при изменении даты списания
        private void dpWriteOffDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dpWriteOffDate.SelectedDate.HasValue && cmbTool.SelectedItem is ToolItem selectedTool)
            {
                decimal? residualValue = CalculateResidualValue(selectedTool, dpWriteOffDate.SelectedDate.Value);

                if (residualValue.HasValue)
                {
                    txtResidualValue.Text = residualValue.Value.ToString("F2",
                        System.Globalization.CultureInfo.InvariantCulture);
                    statusText.Text = $"Дата списания: {dpWriteOffDate.SelectedDate.Value:dd.MM.yyyy}, " +
                                      $"остаточная стоимость: {residualValue.Value:F2} руб.";
                }
                else
                {
                    txtResidualValue.Clear();
                    statusText.Text = $"Дата списания изменена, но недостаточно данных для расчёта";
                }
            }
        }

        private void ClearForm()
        {
            cmbTool.SelectedItem = null;
            cmbEmployee.SelectedItem = null;
            cmbReason.SelectedItem = null;
            cmbReason.Text = "";
            txtResidualValue.Clear();
            txtCalcInfo.Text = "";
            dpWriteOffDate.SelectedDate = DateTime.Today;
            _selectedId = -1;
            btnEdit.IsEnabled = false;
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

        private bool ValidateReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                MessageBox.Show("Пожалуйста, укажите причину списания.", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                cmbReason.Focus();
                return false;
            }
            if (reason.Length > 255)
            {
                MessageBox.Show("Причина списания не должна превышать 255 символов.", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                cmbReason.Focus();
                return false;
            }
            return true;
        }

        private bool ValidateResidualValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true; // Необязательное поле
            }

            if (decimal.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out decimal result))
            {
                if (result < 0 || result > 999999.99m)
                {
                    MessageBox.Show("Остаточная стоимость должна быть в диапазоне от 0 до 999999.99.",
                        "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtResidualValue.Focus();
                    return false;
                }
                return true;
            }
            else
            {
                MessageBox.Show("Неверный формат остаточной стоимости. Используйте цифры и точку как разделитель.",
                    "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtResidualValue.Focus();
                return false;
            }
        }

        private bool ValidateWriteOffDate()
        {
            if (dpWriteOffDate.SelectedDate == null)
            {
                MessageBox.Show("Пожалуйста, выберите дату списания.", "Ошибка валидации",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                dpWriteOffDate.Focus();
                return false;
            }
            return true;
        }

        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Валидация
                if (!ValidateToolSelected()) return;
                if (!ValidateEmployeeSelected()) return;
                if (!ValidateReason(cmbReason.Text)) return;
                if (!ValidateResidualValue(txtResidualValue.Text)) return;
                if (!ValidateWriteOffDate()) return;

                var toolItem = (ToolItem)cmbTool.SelectedItem;
                var employeeItem = (EmployeeItem)cmbEmployee.SelectedItem;

                decimal? residualValue = null;
                if (!string.IsNullOrWhiteSpace(txtResidualValue.Text))
                {
                    residualValue = decimal.Parse(txtResidualValue.Text,
                        System.Globalization.CultureInfo.InvariantCulture);
                }

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    // Обновляем статус инструмента на "списан"
                    var updateToolCmd = new NpgsqlCommand(@"
                        UPDATE public.tools 
                        SET status = 'списан', condition_status = 'списан'
                        WHERE id = @tool_id", conn);
                    updateToolCmd.Parameters.AddWithValue("@tool_id", toolItem.Id);
                    updateToolCmd.ExecuteNonQuery();

                    // Добавляем запись о списании
                    var cmd = new NpgsqlCommand(@"
                        INSERT INTO public.write_offs (tool_id, employee_id, reason, residual_value, write_off_date)
                        VALUES (@tool_id, @employee_id, @reason, @residual_value, @write_off_date)", conn);

                    cmd.Parameters.AddWithValue("@tool_id", toolItem.Id);
                    cmd.Parameters.AddWithValue("@employee_id", employeeItem.Id);
                    cmd.Parameters.AddWithValue("@reason", cmbReason.Text);
                    cmd.Parameters.AddWithValue("@residual_value",
                        residualValue.HasValue ? (object)residualValue.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@write_off_date", dpWriteOffDate.SelectedDate.Value);

                    cmd.ExecuteNonQuery();
                }

                LoadWriteOffs();
                ClearForm();
                statusText.Text = "Запись успешно добавлена, инструмент помечен как списанный";
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

            try
            {
                // Валидация
                if (!ValidateToolSelected()) return;
                if (!ValidateEmployeeSelected()) return;
                if (!ValidateReason(cmbReason.Text)) return;
                if (!ValidateResidualValue(txtResidualValue.Text)) return;
                if (!ValidateWriteOffDate()) return;

                var result = MessageBox.Show("Вы уверены, что хотите сохранить изменения?",
                    "Подтверждение редактирования", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                var toolItem = (ToolItem)cmbTool.SelectedItem;
                var employeeItem = (EmployeeItem)cmbEmployee.SelectedItem;

                decimal? residualValue = null;
                if (!string.IsNullOrWhiteSpace(txtResidualValue.Text))
                {
                    residualValue = decimal.Parse(txtResidualValue.Text,
                        System.Globalization.CultureInfo.InvariantCulture);
                }

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    var cmd = new NpgsqlCommand(@"
                        UPDATE public.write_offs
                        SET tool_id=@tool_id, employee_id=@employee_id, reason=@reason, 
                            residual_value=@residual_value, write_off_date=@write_off_date
                        WHERE id=@id", conn);

                    cmd.Parameters.AddWithValue("@tool_id", toolItem.Id);
                    cmd.Parameters.AddWithValue("@employee_id", employeeItem.Id);
                    cmd.Parameters.AddWithValue("@reason", cmbReason.Text);
                    cmd.Parameters.AddWithValue("@residual_value",
                        residualValue.HasValue ? (object)residualValue.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@write_off_date", dpWriteOffDate.SelectedDate.Value);
                    cmd.Parameters.AddWithValue("@id", _selectedId);

                    cmd.ExecuteNonQuery();
                }

                LoadWriteOffs();
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

        private void dgWriteOffs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgWriteOffs.SelectedItem is WriteOffItem selected)
            {
                _selectedId = selected.Id;
                btnEdit.IsEnabled = true;

                // Заполнение формы
                var tool = ((ObservableCollection<ToolItem>)cmbTool.ItemsSource)
                    .FirstOrDefault(t => t.Id == selected.ToolId);
                cmbTool.SelectedItem = tool;

                var employee = ((ObservableCollection<EmployeeItem>)cmbEmployee.ItemsSource)
                    .FirstOrDefault(emp => emp.Id == selected.EmployeeId);
                cmbEmployee.SelectedItem = employee;

                cmbReason.Text = selected.Reason;
                txtResidualValue.Text = selected.ResidualValue?.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) ?? "";
                dpWriteOffDate.SelectedDate = selected.WriteOffDate;

                statusText.Text = $"Редактирование записи #{selected.Id}";
            }
            else
            {
                btnEdit.IsEnabled = false;
            }
        }

        // Валидация ввода для числовых полей
        private void TextBox_PreviewTextInput_Numeric(object sender, TextCompositionEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            string newText = textBox.Text.Insert(textBox.CaretIndex, e.Text);

            // Проверка: только цифры и одна точка
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