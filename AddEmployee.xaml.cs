using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Npgsql;

namespace Rental
{
    public partial class AddEmployee : Window
    {
        private readonly string _connectionString;
        private ObservableCollection<Employee> _employees;
        private int _selectedId = -1;

        public AddEmployee()
        {
            InitializeComponent();

            var app = (App)Application.Current;
            _connectionString = app.connString;

            _employees = new ObservableCollection<Employee>();
            dgEmployees.ItemsSource = _employees;

            LoadEmployees();
        }

        #region Методы загрузки данных

        private void LoadEmployees()
        {
            try
            {
                _employees.Clear();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"SELECT id, fio, role, passport_series, passport_number, 
                                            issued_by, department_code, datebirth, phone 
                                     FROM public.employees ORDER BY id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var employee = new Employee
                            {
                                Id = reader.GetInt32(0),
                                Fio = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                Role = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                PassportSeries = reader.IsDBNull(3) ? "" : reader.GetString(3),
                                PassportNumber = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                IssuedBy = reader.IsDBNull(5) ? "" : reader.GetString(5),
                                DepartmentCode = reader.IsDBNull(6) ? "" : reader.GetString(6),
                                DateBirth = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
                                Phone = reader.IsDBNull(8) ? "" : reader.GetString(8)
                            };
                            _employees.Add(employee);
                        }
                    }
                }

                UpdateStatus($"Загружено записей: {_employees.Count}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Ошибка загрузки данных");
            }
        }

        #endregion

        #region Методы валидации

        private string GetCleanPhone()
        {
            if (string.IsNullOrEmpty(txtPhone.Text))
                return "";
            return Regex.Replace(txtPhone.Text, @"[^\d]", "");
        }

        private bool ValidateFIO(string fio)
        {
            if (string.IsNullOrWhiteSpace(fio))
            {
                MessageBox.Show("Поле 'ФИО' обязательно для заполнения!", "Предупреждение",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (fio.Length < 5)
            {
                MessageBox.Show("ФИО должно содержать минимум 5 символов!", "Предупреждение",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private bool ValidateRole(string role)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                MessageBox.Show("Поле 'Должность' обязательно для заполнения!", "Предупреждение",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private bool ValidatePhone(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return true;

            string digits = Regex.Replace(phone, @"[^\d]", "");

            if (digits.Length != 11 && digits.Length != 0)
            {
                MessageBox.Show("Телефон должен содержать 11 цифр!\nФормат: +7 (999) 999-99-99",
                                "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private bool ValidatePassportSeries(string series)
        {
            if (string.IsNullOrWhiteSpace(series)) return true;

            string cleanSeries = Regex.Replace(series, @"[^\d]", "");
            if (cleanSeries.Length != 4 && cleanSeries.Length != 0)
            {
                MessageBox.Show("Серия паспорта должна содержать 4 цифры!", "Предупреждение",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private bool ValidatePassportNumber(string number)
        {
            if (string.IsNullOrWhiteSpace(number)) return true;

            string cleanNumber = Regex.Replace(number, @"[^\d]", "");
            if (cleanNumber.Length != 6 && cleanNumber.Length != 0)
            {
                MessageBox.Show("Номер паспорта должен содержать 6 цифр!", "Предупреждение",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private bool ValidateDepartmentCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return true;

            string cleanCode = Regex.Replace(code, @"[^\d]", "");
            if (cleanCode.Length != 6 && cleanCode.Length != 0)
            {
                MessageBox.Show("Код подразделения должен содержать 6 цифр!\nФормат: XXX-XXX",
                                "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private bool ValidateAllFields()
        {
            return ValidateFIO(txtFIO.Text) &&
                   ValidateRole(cmbRole.Text) &&
                   ValidatePhone(txtPhone.Text) &&
                   ValidatePassportSeries(txtPassportSeries.Text) &&
                   ValidatePassportNumber(txtPassportNumber.Text) &&
                   ValidateDepartmentCode(txtDepartmentCode.Text);
        }

        #endregion

        #region Методы формы

        private void ClearForm()
        {
            txtFIO.Clear();
            cmbRole.SelectedIndex = -1;
            cmbRole.Text = "";
            txtPhone.Text = "";
            txtPassportSeries.Text = "";
            txtPassportNumber.Text = "";
            txtIssuedBy.Clear();
            txtDepartmentCode.Text = "";
            dpDateBirth.SelectedDate = new DateTime(1990, 1, 1);
            _selectedId = -1;
            UpdateStatus("Форма очищена");
        }

        private void UpdateStatus(string message)
        {
            txtStatus.Text = message;
        }

        private void FillFormFromSelected()
        {
            if (_selectedId == -1) return;

            var employee = _employees.FirstOrDefault(e => e.Id == _selectedId);
            if (employee != null)
            {
                txtFIO.Text = employee.Fio;
                cmbRole.Text = employee.Role;

                // Форматируем телефон для отображения в маске
                if (!string.IsNullOrEmpty(employee.Phone) && employee.Phone.Length == 11)
                {
                    string phone = employee.Phone;
                    txtPhone.Text = $"+7 ({phone.Substring(1, 3)}) {phone.Substring(4, 3)}-{phone.Substring(7, 2)}-{phone.Substring(9, 2)}";
                }
                else
                {
                    txtPhone.Text = employee.Phone;
                }

                txtPassportSeries.Text = employee.PassportSeries;
                txtPassportNumber.Text = employee.PassportNumber;
                txtIssuedBy.Text = employee.IssuedBy;

                // Форматируем код подразделения для отображения в маске
                if (!string.IsNullOrEmpty(employee.DepartmentCode) && employee.DepartmentCode.Length == 6)
                {
                    txtDepartmentCode.Text = $"{employee.DepartmentCode.Substring(0, 3)}-{employee.DepartmentCode.Substring(3, 3)}";
                }
                else
                {
                    txtDepartmentCode.Text = employee.DepartmentCode;
                }

                dpDateBirth.SelectedDate = employee.DateBirth;
            }
        }

        #endregion

        #region CRUD операции

        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateAllFields()) return;

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"INSERT INTO public.employees 
                                    (fio, role, passport_series, passport_number, 
                                     issued_by, department_code, datebirth, phone)
                                    VALUES (@fio, @role, @passport_series, @passport_number,
                                            @issued_by, @department_code, @datebirth, @phone)";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@fio", txtFIO.Text);
                        cmd.Parameters.AddWithValue("@role", cmbRole.Text);

                        string passportSeries = Regex.Replace(txtPassportSeries.Text, @"[^\d]", "");
                        string passportNumber = Regex.Replace(txtPassportNumber.Text, @"[^\d]", "");
                        string departmentCode = Regex.Replace(txtDepartmentCode.Text, @"[^\d]", "");
                        string phone = GetCleanPhone();

                        cmd.Parameters.AddWithValue("@passport_series",
                            string.IsNullOrWhiteSpace(passportSeries) ? DBNull.Value : (object)passportSeries);
                        cmd.Parameters.AddWithValue("@passport_number",
                            string.IsNullOrWhiteSpace(passportNumber) ? DBNull.Value : (object)passportNumber);
                        cmd.Parameters.AddWithValue("@issued_by",
                            string.IsNullOrWhiteSpace(txtIssuedBy.Text) ? DBNull.Value : (object)txtIssuedBy.Text);
                        cmd.Parameters.AddWithValue("@department_code",
                            string.IsNullOrWhiteSpace(departmentCode) ? DBNull.Value : (object)departmentCode);
                        cmd.Parameters.AddWithValue("@datebirth",
                            dpDateBirth.SelectedDate.HasValue ? (object)dpDateBirth.SelectedDate.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@phone",
                            string.IsNullOrWhiteSpace(phone) ? DBNull.Value : (object)phone);

                        cmd.ExecuteNonQuery();
                    }
                }

                LoadEmployees();
                ClearForm();
                MessageBox.Show("Сотрудник успешно добавлен!", "Успех",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateStatus("Сотрудник добавлен");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при добавлении: {ex.Message}", "Ошибка",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Ошибка при добавлении");
            }
        }

        private void btnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedId == -1)
            {
                MessageBox.Show("Выберите запись для редактирования!", "Предупреждение",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ValidateAllFields()) return;

            var result = MessageBox.Show("Вы уверены, что хотите сохранить изменения?",
                                         "Подтверждение", MessageBoxButton.YesNo,
                                         MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"UPDATE public.employees SET 
                                    fio = @fio,
                                    role = @role,
                                    passport_series = @passport_series,
                                    passport_number = @passport_number,
                                    issued_by = @issued_by,
                                    department_code = @department_code,
                                    datebirth = @datebirth,
                                    phone = @phone
                                    WHERE id = @id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", _selectedId);
                        cmd.Parameters.AddWithValue("@fio", txtFIO.Text);
                        cmd.Parameters.AddWithValue("@role", cmbRole.Text);

                        string passportSeries = Regex.Replace(txtPassportSeries.Text, @"[^\d]", "");
                        string passportNumber = Regex.Replace(txtPassportNumber.Text, @"[^\d]", "");
                        string departmentCode = Regex.Replace(txtDepartmentCode.Text, @"[^\d]", "");
                        string phone = GetCleanPhone();

                        cmd.Parameters.AddWithValue("@passport_series",
                            string.IsNullOrWhiteSpace(passportSeries) ? DBNull.Value : (object)passportSeries);
                        cmd.Parameters.AddWithValue("@passport_number",
                            string.IsNullOrWhiteSpace(passportNumber) ? DBNull.Value : (object)passportNumber);
                        cmd.Parameters.AddWithValue("@issued_by",
                            string.IsNullOrWhiteSpace(txtIssuedBy.Text) ? DBNull.Value : (object)txtIssuedBy.Text);
                        cmd.Parameters.AddWithValue("@department_code",
                            string.IsNullOrWhiteSpace(departmentCode) ? DBNull.Value : (object)departmentCode);
                        cmd.Parameters.AddWithValue("@datebirth",
                            dpDateBirth.SelectedDate.HasValue ? (object)dpDateBirth.SelectedDate.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@phone",
                            string.IsNullOrWhiteSpace(phone) ? DBNull.Value : (object)phone);

                        cmd.ExecuteNonQuery();
                    }
                }

                LoadEmployees();
                ClearForm();
                MessageBox.Show("Данные успешно обновлены!", "Успех",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateStatus("Данные обновлены");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при обновлении: {ex.Message}", "Ошибка",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Ошибка при обновлении");
            }
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
        }

        #endregion

        #region Обработчики событий

        private void dgEmployees_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (dgEmployees.SelectedItem is Employee selected)
            {
                _selectedId = selected.Id;
                FillFormFromSelected();
                UpdateStatus($"Выбран сотрудник: {selected.Fio}");
            }
        }

        private void FIO_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^а-яА-Яa-zA-Z\\s\\-]");
            e.Handled = regex.IsMatch(e.Text);
        }

        #endregion
    }

    #region Класс Employee

    public class Employee
    {
        public int Id { get; set; }
        public string Fio { get; set; } = "";
        public string Role { get; set; } = "";
        public string PassportSeries { get; set; } = "";
        public string PassportNumber { get; set; } = "";
        public string IssuedBy { get; set; } = "";
        public string DepartmentCode { get; set; } = "";
        public DateTime? DateBirth { get; set; }
        public string Phone { get; set; } = "";
    }

    #endregion
}