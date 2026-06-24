using System;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Npgsql;

namespace Rental
{
    public partial class AddSupplier : Window
    {
        private string connString;
        private ObservableCollection<Supplier> suppliersList;
        private int selectedSupplierId = -1;

        public class Supplier
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Inn { get; set; }
            public string Kpp { get; set; }
            public string Ogrn { get; set; }
            public string LegalAddress { get; set; }
            public string Phone { get; set; }
            public string Email { get; set; }
            public string ContactPerson { get; set; }
        }

        public AddSupplier()
        {
            InitializeComponent();

            var app = (App)Application.Current;
            connString = app.connString;

            suppliersList = new ObservableCollection<Supplier>();
            dgSuppliers.ItemsSource = suppliersList;

            LoadSuppliers();
        }

        private void LoadSuppliers()
        {
            try
            {
                suppliersList.Clear();

                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = "SELECT id, name, inn, kpp, ogrn, legal_address, phone, email, contact_person " +
                                   "FROM public.suppliers ORDER BY id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            suppliersList.Add(new Supplier
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                Inn = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                Kpp = reader.IsDBNull(3) ? "" : reader.GetString(3),
                                Ogrn = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                LegalAddress = reader.IsDBNull(5) ? "" : reader.GetString(5),
                                Phone = reader.IsDBNull(6) ? "" : reader.GetString(6),
                                Email = reader.IsDBNull(7) ? "" : reader.GetString(7),
                                ContactPerson = reader.IsDBNull(8) ? "" : reader.GetString(8)
                            });
                        }
                    }
                }

                txtStatus.Text = $"✅ Загружено {suppliersList.Count} поставщиков";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Text = "❌ Ошибка загрузки";
            }
        }

        // Запрещаем ввод букв (только цифры)
        private void txtNumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]");
            e.Handled = regex.IsMatch(e.Text);
        }

        // Запрещаем ввод цифр (только буквы, пробелы, дефисы)
        private void txtLetterOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex(@"[^а-яА-Яa-zA-Z\s\-]");
            e.Handled = regex.IsMatch(e.Text);
        }

        private bool ValidateINN(string inn)
        {
            if (string.IsNullOrEmpty(inn)) return true;
            return inn.Length == 10 || inn.Length == 12;
        }

        private bool ValidateKPP(string kpp)
        {
            if (string.IsNullOrEmpty(kpp)) return true;
            return kpp.Length == 9;
        }

        private bool ValidateOGRN(string ogrn)
        {
            if (string.IsNullOrEmpty(ogrn)) return true;
            return ogrn.Length == 13 || ogrn.Length == 15;
        }

        private bool ValidatePhone(string phone)
        {
            if (string.IsNullOrEmpty(phone)) return true;
            return phone.Length == 11;
        }

        private bool ValidateEmail(string email)
        {
            if (string.IsNullOrEmpty(email)) return true;
            return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        }

        private void ClearForm()
        {
            txtName.Text = "";
            txtINN.Text = "";
            txtKPP.Text = "";
            txtOGRN.Text = "";
            txtLegalAddress.Text = "";
            txtPhone.Text = "";
            txtEmail.Text = "";
            txtContactPerson.Text = "";
            selectedSupplierId = -1;
            txtStatus.Text = "🔄 Форма очищена";
        }

        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show("Введите название поставщика!", "Предупреждение",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                txtName.Focus();
                return;
            }

            string inn = txtINN.Text.Trim();
            string kpp = txtKPP.Text.Trim();
            string ogrn = txtOGRN.Text.Trim();
            string phone = txtPhone.Text.Trim();

            if (!ValidateINN(inn))
            {
                MessageBox.Show("ИНН должен содержать 10 или 12 цифр!", "Ошибка валидации",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                txtINN.Focus();
                return;
            }

            if (!ValidateKPP(kpp))
            {
                MessageBox.Show("КПП должен содержать 9 цифр!", "Ошибка валидации",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                txtKPP.Focus();
                return;
            }

            if (!ValidateOGRN(ogrn))
            {
                MessageBox.Show("ОГРН должен содержать 13 или 15 цифр!", "Ошибка валидации",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                txtOGRN.Focus();
                return;
            }

            if (!ValidatePhone(phone))
            {
                MessageBox.Show("Телефон должен содержать 11 цифр!", "Ошибка валидации",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                txtPhone.Focus();
                return;
            }

            if (!ValidateEmail(txtEmail.Text))
            {
                MessageBox.Show("Введите корректную почту (например: example@domain.com)!", "Ошибка валидации",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                txtEmail.Focus();
                return;
            }

            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"INSERT INTO public.suppliers 
                (name, inn, kpp, ogrn, legal_address, phone, email, contact_person)
                VALUES (@name, @inn, @kpp, @ogrn, @legal_address, @phone, @email, @contact_person)";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@name", txtName.Text.Trim());
                        cmd.Parameters.AddWithValue("@inn", string.IsNullOrEmpty(inn) ? DBNull.Value : (object)inn);
                        cmd.Parameters.AddWithValue("@kpp", string.IsNullOrEmpty(kpp) ? DBNull.Value : (object)kpp);
                        cmd.Parameters.AddWithValue("@ogrn", string.IsNullOrEmpty(ogrn) ? DBNull.Value : (object)ogrn);
                        cmd.Parameters.AddWithValue("@legal_address", string.IsNullOrEmpty(txtLegalAddress.Text) ? DBNull.Value : (object)txtLegalAddress.Text.Trim());
                        cmd.Parameters.AddWithValue("@phone", string.IsNullOrEmpty(phone) ? DBNull.Value : (object)phone);
                        cmd.Parameters.AddWithValue("@email", string.IsNullOrEmpty(txtEmail.Text) ? DBNull.Value : (object)txtEmail.Text.Trim());
                        cmd.Parameters.AddWithValue("@contact_person", string.IsNullOrEmpty(txtContactPerson.Text) ? DBNull.Value : (object)txtContactPerson.Text.Trim());

                        cmd.ExecuteNonQuery();
                    }
                }

                LoadSuppliers();
                ClearForm();
                txtStatus.Text = "✅ Поставщик успешно добавлен";
                MessageBox.Show("Поставщик успешно добавлен!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при добавлении: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Text = "❌ Ошибка при добавлении";
            }
        }

        private void btnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (selectedSupplierId == -1)
            {
                MessageBox.Show("Выберите поставщика для редактирования!", "Предупреждение",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show("Введите название поставщика!", "Предупреждение",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string inn = txtINN.Text.Trim();
            string kpp = txtKPP.Text.Trim();
            string ogrn = txtOGRN.Text.Trim();
            string phone = txtPhone.Text.Trim();

            if (!ValidateINN(inn))
            {
                MessageBox.Show("ИНН должен содержать 10 или 12 цифр!", "Ошибка валидации",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                txtINN.Focus();
                return;
            }

            if (!ValidateKPP(kpp))
            {
                MessageBox.Show("КПП должен содержать 9 цифр!", "Ошибка валидации",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                txtKPP.Focus();
                return;
            }

            if (!ValidateOGRN(ogrn))
            {
                MessageBox.Show("ОГРН должен содержать 13 или 15 цифр!", "Ошибка валидации",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                txtOGRN.Focus();
                return;
            }

            if (!ValidatePhone(phone))
            {
                MessageBox.Show("Телефон должен содержать 11 цифр!", "Ошибка валидации",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                txtPhone.Focus();
                return;
            }

            if (!ValidateEmail(txtEmail.Text))
            {
                MessageBox.Show("Введите корректный email (например: example@domain.com)!", "Ошибка валидации",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                txtEmail.Focus();
                return;
            }

            var result = MessageBox.Show("Вы уверены, что хотите сохранить изменения?", "Подтверждение",
                                        MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"UPDATE public.suppliers SET 
                                    name = @name,
                                    inn = @inn,
                                    kpp = @kpp,
                                    ogrn = @ogrn,
                                    legal_address = @legal_address,
                                    phone = @phone,
                                    email = @email,
                                    contact_person = @contact_person
                                    WHERE id = @id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", selectedSupplierId);
                        cmd.Parameters.AddWithValue("@name", txtName.Text.Trim());
                        cmd.Parameters.AddWithValue("@inn", string.IsNullOrEmpty(inn) ? DBNull.Value : (object)inn);
                        cmd.Parameters.AddWithValue("@kpp", string.IsNullOrEmpty(kpp) ? DBNull.Value : (object)kpp);
                        cmd.Parameters.AddWithValue("@ogrn", string.IsNullOrEmpty(ogrn) ? DBNull.Value : (object)ogrn);
                        cmd.Parameters.AddWithValue("@legal_address", string.IsNullOrEmpty(txtLegalAddress.Text) ? DBNull.Value : (object)txtLegalAddress.Text.Trim());
                        cmd.Parameters.AddWithValue("@phone", string.IsNullOrEmpty(phone) ? DBNull.Value : (object)phone);
                        cmd.Parameters.AddWithValue("@email", string.IsNullOrEmpty(txtEmail.Text) ? DBNull.Value : (object)txtEmail.Text.Trim());
                        cmd.Parameters.AddWithValue("@contact_person", string.IsNullOrEmpty(txtContactPerson.Text) ? DBNull.Value : (object)txtContactPerson.Text.Trim());

                        cmd.ExecuteNonQuery();
                    }
                }

                LoadSuppliers();
                ClearForm();
                txtStatus.Text = "✅ Данные поставщика обновлены";
                MessageBox.Show("Данные успешно обновлены!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при обновлении: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Text = "❌ Ошибка при обновлении";
            }
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
        }

        private void dgSuppliers_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (dgSuppliers.SelectedItem is Supplier selectedSupplier)
            {
                selectedSupplierId = selectedSupplier.Id;
                txtName.Text = selectedSupplier.Name;
                txtINN.Text = selectedSupplier.Inn;
                txtKPP.Text = selectedSupplier.Kpp;
                txtOGRN.Text = selectedSupplier.Ogrn;
                txtLegalAddress.Text = selectedSupplier.LegalAddress;
                txtPhone.Text = selectedSupplier.Phone;
                txtEmail.Text = selectedSupplier.Email;
                txtContactPerson.Text = selectedSupplier.ContactPerson;

                txtStatus.Text = $"📌 Выбран поставщик: {selectedSupplier.Name}";
            }
        }
    }
}