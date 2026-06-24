using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using Npgsql;

namespace Rental
{
    /// <summary>
    /// Форма просмотра договора аренды (только для чтения)
    /// </summary>
    public partial class RentalViewForm : Window
    {
        private string connString;
        private int rentalId;

        /// <summary>
        /// Модель инструмента для отображения
        /// </summary>
        public class ToolItemForView
        {
            public string ToolInventoryNumber { get; set; }
            public decimal PricePerDay { get; set; }
            public DateTime? ReturnDate { get; set; }
            public bool IsDamaged { get; set; }
        }

        /// <summary>
        /// Модель штрафа для отображения
        /// </summary>
        public class FineItemForView
        {
            public string FineType { get; set; }
            public decimal Amount { get; set; }
            public DateTime CreatedAt { get; set; }
            public string Description { get; set; }
            public string ToolName { get; set; }
        }

        public RentalViewForm(int rentalId)
        {
            InitializeComponent();

            var app = (App)Application.Current;
            connString = app.connString;
            this.rentalId = rentalId;

            LoadContractData();
            LoadDelivery();
            LoadTools();
            LoadFines();
            UpdateOverdueInfo();
        }

        /// <summary>
        /// Загрузка основной информации о договоре
        /// </summary>
        private void LoadContractData()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"SELECT 
                                        r.id,
                                        c.name as client_name,
                                        e.fio as employee_fio,
                                        r.start_date,
                                        r.end_date,
                                        r.status,
                                        r.total_price
                                    FROM public.rentals r
                                    LEFT JOIN public.clients c ON r.client_id = c.id
                                    LEFT JOIN public.employees e ON r.employee_id = e.id
                                    WHERE r.id = @id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", rentalId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                txtId.Text = reader.GetInt32(0).ToString();
                                txtClient.Text = reader.GetString(1);
                                txtEmployee.Text = reader.GetString(2);

                                var startDate = reader.GetDateTime(3);
                                var endDate = reader.GetDateTime(4);
                                txtPeriod.Text = $"{startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}";

                                int days = (endDate - startDate).Days;
                                txtDaysCount.Text = days > 0 ? days.ToString() : "1";

                                string status = reader.GetString(5);
                                txtStatus.Text = status;

                                if (status == "активна")
                                    txtStatus.Foreground = Brushes.Green;
                                else if (status == "завершена")
                                    txtStatus.Foreground = Brushes.Blue;
                                else
                                    txtStatus.Foreground = Brushes.Red;

                                txtTotalPrice.Text = reader.IsDBNull(6) ? "0.00" : reader.GetDecimal(6).ToString("N2");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки договора: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Загрузка информации о доставке
        /// </summary>
        private void LoadDelivery()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"SELECT 
                                        dc.city_name,
                                        d.status,
                                        d.delivery_address,
                                        d.delivery_date,
                                        d.cost
                                    FROM public.deliveries d
                                    LEFT JOIN public.delivery_cities dc ON d.city_id = dc.id
                                    WHERE d.rental_id = @rental_id
                                    ORDER BY d.id DESC
                                    LIMIT 1";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@rental_id", rentalId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                txtCity.Text = reader.IsDBNull(0) ? "Не указан" : reader.GetString(0);
                                txtDeliveryStatus.Text = reader.IsDBNull(1) ? "Не указан" : reader.GetString(1);
                                txtDeliveryAddress.Text = reader.IsDBNull(2) ? "Не указан" : reader.GetString(2);

                                if (!reader.IsDBNull(3))
                                    txtDeliveryDate.Text = reader.GetDateTime(3).ToString("dd.MM.yyyy");
                                else
                                    txtDeliveryDate.Text = "Не указана";

                                txtDeliveryCost.Text = reader.IsDBNull(4) ? "0.00 ₽" : $"{reader.GetDecimal(4):N2} ₽";
                            }
                            else
                            {
                                txtCity.Text = "Нет данных";
                                txtDeliveryStatus.Text = "Нет данных";
                                txtDeliveryAddress.Text = "Нет данных";
                                txtDeliveryDate.Text = "Нет данных";
                                txtDeliveryCost.Text = "0.00 ₽";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки доставки: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Загрузка списка арендованных инструментов
        /// </summary>
        private void LoadTools()
        {
            try
            {
                var tools = new ObservableCollection<ToolItemForView>();

                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"SELECT 
                                        t.inventory_number,
                                        ri.price_per_day,
                                        ri.return_date,
                                        ri.is_damaged
                                    FROM public.rental_items ri
                                    JOIN public.tools t ON ri.tool_id = t.id
                                    WHERE ri.rental_id = @rental_id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@rental_id", rentalId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                tools.Add(new ToolItemForView
                                {
                                    ToolInventoryNumber = reader.GetString(0),
                                    PricePerDay = reader.GetDecimal(1),
                                    ReturnDate = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2),
                                    IsDamaged = reader.GetBoolean(3)
                                });
                            }
                        }
                    }
                }

                dgTools.ItemsSource = tools;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки инструментов: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Загрузка списка штрафов из таблицы fines
        /// </summary>
        private void LoadFines()
        {
            try
            {
                var fines = new ObservableCollection<FineItemForView>();
                decimal totalOverdueFine = 0;
                decimal totalDamageFine = 0;

                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"SELECT 
                                        f.type, 
                                        f.amount, 
                                        f.created_at,
                                        t.inventory_number as tool_name
                                    FROM public.fines f
                                    LEFT JOIN public.rental_items ri ON f.rental_id = ri.rental_id
                                    LEFT JOIN public.tools t ON ri.tool_id = t.id
                                    WHERE f.rental_id = @rental_id
                                    ORDER BY f.created_at DESC";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@rental_id", rentalId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string fineType = reader.GetString(0);  // "просрочка" или "повреждение"
                                decimal amount = reader.GetDecimal(1);
                                string toolName = reader.IsDBNull(3) ? "—" : reader.GetString(3);

                                fines.Add(new FineItemForView
                                {
                                    FineType = GetFineTypeDisplay(fineType),
                                    Amount = amount,
                                    CreatedAt = reader.GetDateTime(2),
                                    Description = GetFineDescription(fineType),
                                    ToolName = toolName
                                });

                                // Суммируем по типам (теперь на русском языке)
                                if (fineType == "просрочка")
                                    totalOverdueFine += amount;
                                else if (fineType == "повреждение")
                                    totalDamageFine += amount;
                            }
                        }
                    }
                }

                dgFines.ItemsSource = fines;

                // Обновляем отображение сводки по штрафам
                txtOverdueFineAmount.Text = totalOverdueFine > 0 ? $"{totalOverdueFine:N2} ₽" : "0.00 ₽";
                txtOverdueFineDesc.Text = totalOverdueFine > 0 ? "Начислен за просрочку возврата" : "Штрафов нет";

                txtDamageFineAmount.Text = totalDamageFine > 0 ? $"{totalDamageFine:N2} ₽" : "0.00 ₽";
                txtDamageFineDesc.Text = totalDamageFine > 0 ? "Начислен за повреждение инструмента" : "Штрафов нет";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки штрафов: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Обновление информации о просрочке
        /// </summary>
        private void UpdateOverdueInfo()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"SELECT end_date FROM public.rentals WHERE id = @id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", rentalId);
                        var result = cmd.ExecuteScalar();

                        if (result != null && result != DBNull.Value)
                        {
                            DateTime endDate = Convert.ToDateTime(result);
                            DateTime currentDate = DateTime.Today;

                            if (currentDate > endDate)
                            {
                                int overdueDays = (currentDate - endDate).Days;
                                txtOverdueInfo.Text = $"⚠️ Договор просрочен на {overdueDays} дней!";
                                txtOverdueInfo.Foreground = Brushes.Red;
                            }
                            else
                            {
                                int daysLeft = (endDate - currentDate).Days;
                                if (daysLeft <= 3 && daysLeft >= 0)
                                {
                                    txtOverdueInfo.Text = $"⚠️ До окончания аренды осталось {daysLeft} дней!";
                                    txtOverdueInfo.Foreground = Brushes.Orange;
                                }
                                else
                                {
                                    txtOverdueInfo.Text = "✅ Просрочки нет";
                                    txtOverdueInfo.Foreground = Brushes.Green;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                txtOverdueInfo.Text = "Не удалось загрузить информацию";
                System.Diagnostics.Debug.WriteLine($"Ошибка: {ex.Message}");
            }
        }

        /// <summary>
        /// Получение отображаемого названия типа штрафа
        /// </summary>
        private string GetFineTypeDisplay(string fineType)
        {
            switch (fineType)
            {
                case "просрочка":
                    return "Просрочка";
                case "повреждение":
                    return "Повреждение";
                default:
                    return fineType;
            }
        }

        /// <summary>
        /// Получение описания штрафа
        /// </summary>
        private string GetFineDescription(string fineType)
        {
            switch (fineType)
            {
                case "просрочка":
                    return "Просрочка возврата инструмента";
                case "повреждение":
                    return "Повреждение инструмента при использовании";
                default:
                    return fineType;
            }
        }

        /// <summary>
        /// Закрытие формы
        /// </summary>
        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}