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
    public class Client
    {
        public int Id { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string PassportSeries { get; set; }
        public string PassportNumber { get; set; }
        public string IssuedBy { get; set; }
        public string DepartmentCode { get; set; }
    }

    public partial class AddClient : Window
    {
        private string _connectionString;
        private ObservableCollection<Client> _clients;
        private int _selectedId = -1;

        public AddClient()
        {
            InitializeComponent();

            if (Application.Current is App app)
            {
                _connectionString = app.connString;
            }

            _clients = new ObservableCollection<Client>();
            dgClients.ItemsSource = _clients;

            LoadClients();
        }

        private void LoadClients()
        {
            try
            {
                _clients.Clear();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string sql = "SELECT id, type, name, phone, email, passport_series, passport_number, issued_by, department_code FROM public.clients ORDER BY id";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            _clients.Add(new Client
                            {
                                Id = reader.GetInt32(0),
                                Type = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                Name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                Phone = reader.IsDBNull(3) ? "" : reader.GetString(3),
                                Email = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                PassportSeries = reader.IsDBNull(5) ? "" : reader.GetString(5),
                                PassportNumber = reader.IsDBNull(6) ? "" : reader.GetString(6),
                                IssuedBy = reader.IsDBNull(7) ? "" : reader.GetString(7),
                                DepartmentCode = reader.IsDBNull(8) ? "" : reader.GetString(8)
                            });
                        }
                    }
                }

                txtStatus.Text = $"Готово. Загружено записей: {_clients.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Text = "Ошибка загрузки данных";
            }
        }

        private string GetCleanPhone()
        {
            if (string.IsNullOrEmpty(txtPhone.Text))
                return "";

            // Удаляем все нецифровые символы
            string cleaned = Regex.Replace(txtPhone.Text, @"[^\d]", "");

            return cleaned;
        }

        private void ClearForm()
        {
            cmbType.SelectedIndex = -1;
            txtName.Text = "";
            txtPhone.Text = "";
            txtEmail.Text = "";
            txtPassportSeries.Text = "";
            txtPassportNumber.Text = "";
            txtIssuedBy.Text = "";
            txtDepartmentCode.Text = "";
            _selectedId = -1;
            txtStatus.Text = "Форма очищена. Готово к добавлению новой записи.";
        }

        private bool ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Поле 'ФИО / Название' обязательно для заполнения!",
                    "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (name.Length > 150)
            {
                MessageBox.Show("ФИО / Название не должно превышать 150 символов!",
                    "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private bool ValidatePhone(string phone)
        {
            if (!string.IsNullOrEmpty(phone))
            {
                string cleaned = Regex.Replace(phone, @"[^\d]", "");
                if (cleaned.Length != 11)
                {
                    MessageBox.Show("Телефон должен содержать ровно 11 цифр! Формат: +7 (999) 999-99-99",
                        "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }
            return true;
        }

        private bool ValidateEmail(string email)
        {
            if (!string.IsNullOrEmpty(email))
            {
                string emailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
                if (!Regex.IsMatch(email, emailPattern))
                {
                    MessageBox.Show("Введите корректный почтовый адрес!",
                        "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }
            return true;
        }

        private bool ValidatePassportSeries(string series)
        {
            if (!string.IsNullOrEmpty(series))
            {
                if (series.Length != 4)
                {
                    MessageBox.Show("Серия паспорта должна содержать ровно 4 цифры!",
                        "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }
            return true;
        }

        private bool ValidatePassportNumber(string number)
        {
            if (!string.IsNullOrEmpty(number))
            {
                if (number.Length != 6)
                {
                    MessageBox.Show("Номер паспорта должен содержать ровно 6 цифр!",
                        "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }
            return true;
        }

        private bool ValidateDepartmentCode(string code)
        {
            if (!string.IsNullOrEmpty(code))
            {
                string cleaned = Regex.Replace(code, @"[^\d]", "");
                if (cleaned.Length != 6)
                {
                    MessageBox.Show("Код подразделения должен содержать ровно 6 цифр!",
                        "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }
            return true;
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateName(txtName.Text)) return;
                if (!ValidatePhone(txtPhone.Text)) return;
                if (!ValidateEmail(txtEmail.Text)) return;
                if (!ValidatePassportSeries(txtPassportSeries.Text)) return;
                if (!ValidatePassportNumber(txtPassportNumber.Text)) return;
                if (!ValidateDepartmentCode(txtDepartmentCode.Text)) return;

                string type = cmbType.SelectedItem != null ? ((ComboBoxItem)cmbType.SelectedItem).Content.ToString() : "";
                string phone = GetCleanPhone();
                string passportSeries = txtPassportSeries.Text;
                string passportNumber = txtPassportNumber.Text;
                string departmentCode = Regex.Replace(txtDepartmentCode.Text, @"[^\d]", "");

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string sql = @"INSERT INTO public.clients (type, name, phone, email, passport_series, passport_number, issued_by, department_code) 
                                   VALUES (@type, @name, @phone, @email, @passport_series, @passport_number, @issued_by, @department_code)";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@type", string.IsNullOrEmpty(type) ? DBNull.Value : (object)type);
                        cmd.Parameters.AddWithValue("@name", txtName.Text);
                        cmd.Parameters.AddWithValue("@phone", string.IsNullOrEmpty(phone) ? DBNull.Value : (object)phone);
                        cmd.Parameters.AddWithValue("@email", string.IsNullOrEmpty(txtEmail.Text) ? DBNull.Value : (object)txtEmail.Text);
                        cmd.Parameters.AddWithValue("@passport_series", string.IsNullOrEmpty(passportSeries) ? DBNull.Value : (object)passportSeries);
                        cmd.Parameters.AddWithValue("@passport_number", string.IsNullOrEmpty(passportNumber) ? DBNull.Value : (object)passportNumber);
                        cmd.Parameters.AddWithValue("@issued_by", string.IsNullOrEmpty(txtIssuedBy.Text) ? DBNull.Value : (object)txtIssuedBy.Text);
                        cmd.Parameters.AddWithValue("@department_code", string.IsNullOrEmpty(departmentCode) ? DBNull.Value : (object)departmentCode);

                        cmd.ExecuteNonQuery();
                    }
                }

                LoadClients();
                ClearForm();
                MessageBox.Show("Клиент успешно добавлен!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при добавлении: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Text = "Ошибка при добавлении";
            }
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedId == -1)
            {
                MessageBox.Show("Выберите запись для редактирования!",
                    "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (!ValidateName(txtName.Text)) return;
                if (!ValidatePhone(txtPhone.Text)) return;
                if (!ValidateEmail(txtEmail.Text)) return;
                if (!ValidatePassportSeries(txtPassportSeries.Text)) return;
                if (!ValidatePassportNumber(txtPassportNumber.Text)) return;
                if (!ValidateDepartmentCode(txtDepartmentCode.Text)) return;

                var result = MessageBox.Show("Вы уверены, что хотите сохранить изменения?",
                    "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;

                string type = cmbType.SelectedItem != null ? ((ComboBoxItem)cmbType.SelectedItem).Content.ToString() : "";
                string phone = GetCleanPhone();
                string passportSeries = txtPassportSeries.Text;
                string passportNumber = txtPassportNumber.Text;
                string departmentCode = Regex.Replace(txtDepartmentCode.Text, @"[^\d]", "");

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string sql = @"UPDATE public.clients 
                                   SET type = @type, 
                                       name = @name, 
                                       phone = @phone, 
                                       email = @email, 
                                       passport_series = @passport_series, 
                                       passport_number = @passport_number, 
                                       issued_by = @issued_by, 
                                       department_code = @department_code 
                                   WHERE id = @id";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", _selectedId);
                        cmd.Parameters.AddWithValue("@type", string.IsNullOrEmpty(type) ? DBNull.Value : (object)type);
                        cmd.Parameters.AddWithValue("@name", txtName.Text);
                        cmd.Parameters.AddWithValue("@phone", string.IsNullOrEmpty(phone) ? DBNull.Value : (object)phone);
                        cmd.Parameters.AddWithValue("@email", string.IsNullOrEmpty(txtEmail.Text) ? DBNull.Value : (object)txtEmail.Text);
                        cmd.Parameters.AddWithValue("@passport_series", string.IsNullOrEmpty(passportSeries) ? DBNull.Value : (object)passportSeries);
                        cmd.Parameters.AddWithValue("@passport_number", string.IsNullOrEmpty(passportNumber) ? DBNull.Value : (object)passportNumber);
                        cmd.Parameters.AddWithValue("@issued_by", string.IsNullOrEmpty(txtIssuedBy.Text) ? DBNull.Value : (object)txtIssuedBy.Text);
                        cmd.Parameters.AddWithValue("@department_code", string.IsNullOrEmpty(departmentCode) ? DBNull.Value : (object)departmentCode);

                        cmd.ExecuteNonQuery();
                    }
                }

                LoadClients();
                ClearForm();
                MessageBox.Show("Данные клиента успешно обновлены!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при редактировании: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Text = "Ошибка при редактировании";
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
        }

        private void DgClients_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgClients.SelectedItem is Client selectedClient)
            {
                _selectedId = selectedClient.Id;

                if (!string.IsNullOrEmpty(selectedClient.Type))
                {
                    foreach (ComboBoxItem item in cmbType.Items)
                    {
                        if (item.Content.ToString() == selectedClient.Type)
                        {
                            cmbType.SelectedItem = item;
                            break;
                        }
                    }
                }
                else
                {
                    cmbType.SelectedIndex = -1;
                }

                txtName.Text = selectedClient.Name;

                // Форматируем телефон для отображения в маске
                if (!string.IsNullOrEmpty(selectedClient.Phone) && selectedClient.Phone.Length == 11)
                {
                    string phone = selectedClient.Phone;
                    txtPhone.Text = $"+7 ({phone.Substring(1, 3)}) {phone.Substring(4, 3)}-{phone.Substring(7, 2)}-{phone.Substring(9, 2)}";
                }
                else
                {
                    txtPhone.Text = selectedClient.Phone;
                }

                txtEmail.Text = selectedClient.Email;
                txtPassportSeries.Text = selectedClient.PassportSeries;
                txtPassportNumber.Text = selectedClient.PassportNumber;
                txtIssuedBy.Text = selectedClient.IssuedBy;

                // Форматируем код подразделения для отображения в маске
                if (!string.IsNullOrEmpty(selectedClient.DepartmentCode) && selectedClient.DepartmentCode.Length == 6)
                {
                    txtDepartmentCode.Text = $"{selectedClient.DepartmentCode.Substring(0, 3)}-{selectedClient.DepartmentCode.Substring(3, 3)}";
                }
                else
                {
                    txtDepartmentCode.Text = selectedClient.DepartmentCode;
                }

                txtStatus.Text = $"Выбрана запись ID: {_selectedId} - {selectedClient.Name}";
            }
        }

        private void TextBoxLettersOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^а-яА-Яa-zA-Z\\s\\-]");
            e.Handled = regex.IsMatch(e.Text);
        }
    }
}