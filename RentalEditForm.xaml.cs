using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Npgsql;

namespace Rental
{
    public partial class RentalEditForm : Window
    {
        private string connString;
        private ObservableCollection<ClientComboItem> clientsList;
        private ObservableCollection<EmployeeComboItem> employeesList;
        private ObservableCollection<CityComboItem> citiesList;
        private ObservableCollection<RentalItemForDisplay> rentalItems;
        private int? editingRentalId = null;
        private bool isEditMode = false;
        private int? existingDeliveryId = null;
        private bool isLoadingData = false;

        public class ClientComboItem
        {
            public int Id { get; set; }
            public string DisplayText { get; set; }
        }

        public class EmployeeComboItem
        {
            public int Id { get; set; }
            public string DisplayText { get; set; }
        }

        public class CityComboItem
        {
            public int Id { get; set; }
            public string CityName { get; set; }
            public decimal DeliveryCost { get; set; }
        }

        public class RentalItemForDisplay
        {
            public int Id { get; set; }
            public int RentalId { get; set; }
            public int ToolId { get; set; }
            public string ToolInventoryNumber { get; set; }
            public decimal PricePerDay { get; set; }
            public DateTime? ReturnDate { get; set; }
            public bool IsDamaged { get; set; }
            public DateTime? PurchaseDate { get; set; }

            public string DiscountText
            {
                get
                {
                    if (!PurchaseDate.HasValue)
                        return "нет данных";

                    var age = DateTime.Today - PurchaseDate.Value;
                    double yearsOld = age.TotalDays / 365.25;

                    if (yearsOld < 3)
                        return "нет (0%)";
                    else if (yearsOld >= 3 && yearsOld < 6)
                        return $"10% ({yearsOld:F1} лет)";
                    else
                        return $"20% ({yearsOld:F1} лет)";
                }
            }

            public decimal PricePerDayWithDiscount
            {
                get
                {
                    if (!PurchaseDate.HasValue)
                        return PricePerDay;

                    var age = DateTime.Today - PurchaseDate.Value;
                    double yearsOld = age.TotalDays / 365.25;

                    if (yearsOld < 3)
                        return PricePerDay;
                    else if (yearsOld >= 3 && yearsOld < 6)
                        return PricePerDay * 0.9m;
                    else
                        return PricePerDay * 0.8m;
                }
            }
        }

        public RentalEditForm(int? rentalId = null)
        {
            InitializeComponent();

            var app = (App)Application.Current;
            connString = app.connString;

            clientsList = new ObservableCollection<ClientComboItem>();
            employeesList = new ObservableCollection<EmployeeComboItem>();
            citiesList = new ObservableCollection<CityComboItem>();
            rentalItems = new ObservableCollection<RentalItemForDisplay>();

            cmbClient.ItemsSource = clientsList;
            cmbEmployee.ItemsSource = employeesList;
            cmbCity.ItemsSource = citiesList;
            dgTools.ItemsSource = rentalItems;

            dgTools.MouseDoubleClick += DgTools_MouseDoubleClick;

            editingRentalId = rentalId;
            isEditMode = rentalId.HasValue;

            LoadReferenceData();

            if (isEditMode)
            {
                Title = "Редактирование договора аренды";
                LoadRentalData();
            }
            else
            {
                Title = "Новый договор аренды";
                SetDefaultValues();
            }
        }

        private decimal GetToolAgeDiscount(DateTime? purchaseDate)
        {
            if (!purchaseDate.HasValue)
                return 1.0m;

            var age = DateTime.Today - purchaseDate.Value;
            double yearsOld = age.TotalDays / 365.25;

            if (yearsOld < 3)
                return 1.0m;
            else if (yearsOld >= 3 && yearsOld < 6)
                return 0.9m;
            else
                return 0.8m;
        }

        private void DgTools_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!isEditMode) return;

            var selectedItem = dgTools.SelectedItem as RentalItemForDisplay;
            if (selectedItem == null) return;

            var editToolWindow = new AddRentalTool(selectedItem.ToolId, selectedItem.PricePerDay, selectedItem.ReturnDate, selectedItem.IsDamaged);
            editToolWindow.Owner = this;

            if (editToolWindow.ShowDialog() == true)
            {
                selectedItem.PricePerDay = editToolWindow.PricePerDay;
                selectedItem.ReturnDate = editToolWindow.ReturnDate;
                selectedItem.IsDamaged = editToolWindow.IsDamaged;

                dgTools.Items.Refresh();
                UpdateTotalPrice();
            }
        }

        private void LoadReferenceData()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();

                    string clientQuery = "SELECT id, name FROM public.clients ORDER BY name";
                    using (var cmd = new NpgsqlCommand(clientQuery, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        clientsList.Clear();
                        while (reader.Read())
                        {
                            clientsList.Add(new ClientComboItem
                            {
                                Id = reader.GetInt32(0),
                                DisplayText = reader.GetString(1)
                            });
                        }
                    }

                    string employeeQuery = "SELECT id, fio FROM public.employees ORDER BY fio";
                    using (var cmd = new NpgsqlCommand(employeeQuery, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        employeesList.Clear();
                        while (reader.Read())
                        {
                            employeesList.Add(new EmployeeComboItem
                            {
                                Id = reader.GetInt32(0),
                                DisplayText = reader.GetString(1)
                            });
                        }
                    }

                    string cityQuery = "SELECT id, city_name, delivery_cost FROM public.delivery_cities ORDER BY city_name";
                    using (var cmd = new NpgsqlCommand(cityQuery, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        citiesList.Clear();
                        while (reader.Read())
                        {
                            citiesList.Add(new CityComboItem
                            {
                                Id = reader.GetInt32(0),
                                CityName = reader.GetString(1),
                                DeliveryCost = reader.GetDecimal(2)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке справочных данных: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadRentalData()
        {
            isLoadingData = true;
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();

                    string rentalQuery = @"SELECT client_id, employee_id, start_date, end_date, status, total_price
                                          FROM public.rentals WHERE id = @id";

                    using (var cmd = new NpgsqlCommand(rentalQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", editingRentalId.Value);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                cmbClient.SelectedValue = reader.IsDBNull(0) ? (object)null : reader.GetInt32(0);
                                cmbEmployee.SelectedValue = reader.IsDBNull(1) ? (object)null : reader.GetInt32(1);
                                dpStartDate.SelectedDate = reader.GetDateTime(2);
                                dpEndDate.SelectedDate = reader.GetDateTime(3);

                                string status = reader.GetString(4);
                                cmbStatus.SelectedItem = cmbStatus.Items.Cast<ComboBoxItem>()
                                    .FirstOrDefault(x => x.Content.ToString() == status);
                            }
                        }
                    }

                    string deliveryQuery = @"SELECT id, city_id, status, delivery_address, delivery_date, cost
                                            FROM public.deliveries WHERE rental_id = @rental_id LIMIT 1";
                    using (var cmd = new NpgsqlCommand(deliveryQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@rental_id", editingRentalId.Value);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                existingDeliveryId = reader.GetInt32(0);
                                cmbCity.SelectedValue = reader.IsDBNull(1) ? (object)null : reader.GetInt32(1);

                                string deliveryStatus = reader.GetString(2);
                                cmbDeliveryStatus.SelectedItem = cmbDeliveryStatus.Items.Cast<ComboBoxItem>()
                                    .FirstOrDefault(x => x.Content.ToString() == deliveryStatus);

                                txtDeliveryAddress.Text = reader.IsDBNull(3) ? "" : reader.GetString(3);
                                dpDeliveryDate.SelectedDate = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4);

                                decimal cost = reader.IsDBNull(5) ? 0 : reader.GetDecimal(5);
                                txtDeliveryCost.Text = cost.ToString("N2");
                            }
                        }
                    }
                }

                LoadRentalItems();
                UpdateDaysCount();
                UpdateTotalPrice();
                UpdateOverdueInfo();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке данных договора: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                isLoadingData = false;
            }
        }

        private void LoadRentalItems()
        {
            try
            {
                rentalItems.Clear();

                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"SELECT ri.id, ri.rental_id, ri.tool_id, t.inventory_number, 
                                           ri.price_per_day, ri.return_date, ri.is_damaged, t.purchase_date
                                    FROM public.rental_items ri
                                    JOIN public.tools t ON ri.tool_id = t.id
                                    WHERE ri.rental_id = @rental_id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@rental_id", editingRentalId.Value);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                rentalItems.Add(new RentalItemForDisplay
                                {
                                    Id = reader.GetInt32(0),
                                    RentalId = reader.GetInt32(1),
                                    ToolId = reader.GetInt32(2),
                                    ToolInventoryNumber = reader.GetString(3),
                                    PricePerDay = reader.GetDecimal(4),
                                    ReturnDate = reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5),
                                    IsDamaged = reader.GetBoolean(6),
                                    PurchaseDate = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке списка инструментов: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetDefaultValues()
        {
            dpStartDate.SelectedDate = DateTime.Today;
            dpEndDate.SelectedDate = DateTime.Today.AddDays(7);

            cmbStatus.IsEnabled = false;
            cmbStatus.SelectedItem = cmbStatus.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(x => x.Content.ToString() == "активна");

            cmbDeliveryStatus.IsEnabled = false;
            cmbDeliveryStatus.SelectedItem = null;

            cmbCity.SelectedItem = null;

            rentalItems.Clear();
            txtDeliveryCost.Text = "0.00";
            txtDeliveryCostTotal.Text = "0.00";
            txtDeliveryAddress.Text = "";
            dpDeliveryDate.SelectedDate = null;

            UpdateDaysCount();
            UpdateTotalPrice();
        }

        private void UpdateDaysCount()
        {
            if (dpStartDate.SelectedDate.HasValue && dpEndDate.SelectedDate.HasValue)
            {
                var days = (dpEndDate.SelectedDate.Value - dpStartDate.SelectedDate.Value).Days;
                if (days <= 0) days = 1;
                txtDaysCount.Text = days.ToString();
            }
            else
            {
                txtDaysCount.Text = "0";
            }
        }

        private decimal GetOverdueCoefficientForTool(int toolId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"SELECT tm.overdue_coefficient 
                                    FROM public.tools t
                                    JOIN public.tool_models tm ON t.model_id = tm.id
                                    WHERE t.id = @tool_id";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@tool_id", toolId);
                        object result = cmd.ExecuteScalar();

                        if (result != null && result != DBNull.Value)
                        {
                            return Convert.ToDecimal(result);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка получения коэффициента: {ex.Message}");
            }

            return 1.0m;
        }

        private decimal CalculateBaseRentalCost()
        {
            if (!dpStartDate.SelectedDate.HasValue || !dpEndDate.SelectedDate.HasValue)
                return 0;

            var startDate = dpStartDate.SelectedDate.Value;
            var endDate = dpEndDate.SelectedDate.Value;
            int plannedDays = (endDate - startDate).Days;
            if (plannedDays <= 0) plannedDays = 1;

            decimal total = 0;
            foreach (var item in rentalItems)
            {
                total += item.PricePerDayWithDiscount * plannedDays;
            }
            return total;
        }

        private decimal CalculateOverdueCost()
        {
            if (!dpEndDate.SelectedDate.HasValue) return 0;

            var endDate = dpEndDate.SelectedDate.Value;
            var currentDate = DateTime.Today;

            if (currentDate <= endDate) return 0;

            int overdueDays = (currentDate - endDate).Days;
            decimal totalOverdue = 0;

            foreach (var item in rentalItems)
            {
                if (item.ReturnDate == null)
                {
                    decimal overdueCoefficient = GetOverdueCoefficientForTool(item.ToolId);
                    decimal overduePricePerDay = item.PricePerDayWithDiscount * overdueCoefficient;
                    totalOverdue += overduePricePerDay * overdueDays;
                }
            }

            return totalOverdue;
        }

        private decimal CalculateTotalRentalCost()
        {
            if (!dpStartDate.SelectedDate.HasValue || !dpEndDate.SelectedDate.HasValue)
                return 0;

            var startDate = dpStartDate.SelectedDate.Value;
            var endDate = dpEndDate.SelectedDate.Value;
            int plannedDays = (endDate - startDate).Days;
            if (plannedDays <= 0) plannedDays = 1;
            DateTime currentDate = DateTime.Today;

            decimal totalRentalCost = 0;

            foreach (var item in rentalItems)
            {
                decimal overdueCoefficient = GetOverdueCoefficientForTool(item.ToolId);
                decimal plannedCost = item.PricePerDayWithDiscount * plannedDays;
                decimal overdueCost = 0;

                if (currentDate > endDate && item.ReturnDate == null)
                {
                    int overdueDays = (currentDate - endDate).Days;
                    decimal overduePricePerDay = item.PricePerDayWithDiscount * overdueCoefficient;
                    overdueCost = overduePricePerDay * overdueDays;
                }

                totalRentalCost += plannedCost + overdueCost;
            }

            return totalRentalCost;
        }

        private void UpdateOverdueInfo()
        {
            if (!dpEndDate.SelectedDate.HasValue) return;

            var endDate = dpEndDate.SelectedDate.Value;
            var currentDate = DateTime.Today;

            if (currentDate > endDate)
            {
                int overdueDays = (currentDate - endDate).Days;
                decimal overdueTotal = CalculateOverdueCost();

                txtOverdueInfo.Text = $"⚠️ Договор просрочен на {overdueDays} дней! " +
                                      $"Штраф за просрочку: {overdueTotal:N2} руб.";
                txtOverdueInfo.Foreground = System.Windows.Media.Brushes.Red;
            }
            else
            {
                int daysLeft = (endDate - currentDate).Days;
                if (daysLeft <= 3 && daysLeft >= 0)
                {
                    txtOverdueInfo.Text = $"⚠️ До окончания аренды осталось {daysLeft} дней! " +
                                          "При просрочке будет применен повышающий коэффициент.";
                    txtOverdueInfo.Foreground = System.Windows.Media.Brushes.Orange;
                }
                else
                {
                    txtOverdueInfo.Text = "✅ Просрочки нет";
                    txtOverdueInfo.Foreground = System.Windows.Media.Brushes.Green;
                }
            }
        }

        private decimal CalculateDamageCost()
        {
            decimal totalDamage = 0;

            foreach (var item in rentalItems)
            {
                if (item.IsDamaged)
                {
                    decimal damageFine = item.PricePerDayWithDiscount * 3;
                    totalDamage += damageFine;
                }
            }

            return totalDamage;
        }

        private decimal GetDeliveryCost()
        {
            if (cmbCity.SelectedItem is CityComboItem selectedCity)
                return selectedCity.DeliveryCost;
            return 0;
        }

        private void UpdateTotalPrice()
        {
            var baseRentalCost = CalculateBaseRentalCost();
            var overdueCost = CalculateOverdueCost();
            var damageCost = CalculateDamageCost();
            var deliveryCost = GetDeliveryCost();

            var total = baseRentalCost + overdueCost + damageCost + deliveryCost;

            txtRentalCost.Text = baseRentalCost.ToString("N2");
            txtOverdueCost.Text = overdueCost.ToString("N2");
            txtDamageCost.Text = damageCost.ToString("N2");
            txtDeliveryCostTotal.Text = deliveryCost.ToString("N2");
            txtTotalPrice.Text = total.ToString("N2");

            txtOverdueCost.Background = overdueCost > 0 ? System.Windows.Media.Brushes.LightPink : System.Windows.Media.Brushes.LightGray;
            txtDamageCost.Background = damageCost > 0 ? System.Windows.Media.Brushes.LightPink : System.Windows.Media.Brushes.LightGray;
        }

        private void btnClearCity_Click(object sender, RoutedEventArgs e)
        {
            cmbCity.SelectedItem = null;
            txtDeliveryCost.Text = "0.00";

            if (!isEditMode)
            {
                cmbDeliveryStatus.IsEnabled = false;
                cmbDeliveryStatus.SelectedItem = null;
            }

            UpdateTotalPrice();
        }

        private void cmbCity_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingData) return;

            if (cmbCity.SelectedItem is CityComboItem selectedCity)
            {
                txtDeliveryCost.Text = selectedCity.DeliveryCost.ToString("N2");

                if (!isEditMode)
                {
                    cmbDeliveryStatus.IsEnabled = true;
                    cmbDeliveryStatus.SelectedItem = cmbDeliveryStatus.Items.Cast<ComboBoxItem>()
                        .FirstOrDefault(x => x.Content.ToString() == "ожидает");
                }

                UpdateTotalPrice();
            }
            else
            {
                txtDeliveryCost.Text = "0.00";

                if (!isEditMode)
                {
                    cmbDeliveryStatus.IsEnabled = false;
                    cmbDeliveryStatus.SelectedItem = null;
                }

                UpdateTotalPrice();
            }
        }

        private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDaysCount();
            UpdateTotalPrice();
            UpdateOverdueInfo();
        }

        private void btnAddTool_Click(object sender, RoutedEventArgs e)
        {
            var addToolWindow = new AddRentalTool(isEditMode);
            addToolWindow.Owner = this;

            if (addToolWindow.ShowDialog() == true)
            {
                if (rentalItems.Any(x => x.ToolId == addToolWindow.SelectedToolId))
                {
                    MessageBox.Show("Этот инструмент уже добавлен в текущий договор!",
                        "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                DateTime? purchaseDate = GetToolPurchaseDate(addToolWindow.SelectedToolId);

                var newItem = new RentalItemForDisplay
                {
                    ToolId = addToolWindow.SelectedToolId,
                    ToolInventoryNumber = addToolWindow.SelectedToolInventoryNumber,
                    PricePerDay = addToolWindow.PricePerDay,
                    ReturnDate = null,
                    IsDamaged = false,
                    PurchaseDate = purchaseDate
                };

                rentalItems.Add(newItem);
                dgTools.Items.Refresh();
                UpdateTotalPrice();

                decimal discountPercent = (1 - GetToolAgeDiscount(purchaseDate)) * 100;
                string discountInfo = discountPercent > 0 ? $"\nСкидка: {discountPercent:F0}%\nЦена со скидкой: {newItem.PricePerDayWithDiscount:N2} ₽/день" : "";

                MessageBox.Show($"Инструмент \"{addToolWindow.SelectedToolInventoryNumber}\" добавлен в договор!{discountInfo}",
                    "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private DateTime? GetToolPurchaseDate(int toolId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = "SELECT purchase_date FROM public.tools WHERE id = @tool_id";
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@tool_id", toolId);
                        object result = cmd.ExecuteScalar();
                        return result == DBNull.Value ? null : (DateTime?)result;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка получения даты покупки: {ex.Message}");
                return null;
            }
        }

        private void btnRemoveTool_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var item = button?.Tag as RentalItemForDisplay;

            if (item != null)
            {
                if (MessageBox.Show($"Удалить инструмент \"{item.ToolInventoryNumber}\" из договора?",
                    "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    rentalItems.Remove(item);
                    UpdateTotalPrice();
                }
            }
        }

        private bool ValidateFields()
        {
            if (cmbClient.SelectedItem == null)
            {
                MessageBox.Show("Выберите клиента!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (cmbEmployee.SelectedItem == null)
            {
                MessageBox.Show("Выберите сотрудника!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!dpStartDate.SelectedDate.HasValue || !dpEndDate.SelectedDate.HasValue)
            {
                MessageBox.Show("Укажите даты начала и окончания аренды!",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (dpEndDate.SelectedDate.Value < dpStartDate.SelectedDate.Value)
            {
                MessageBox.Show("Дата окончания должна быть больше или равна дате начала!",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (rentalItems.Count == 0)
            {
                MessageBox.Show("Добавьте хотя бы один инструмент в договор!",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateFields())
                return;

            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            int rentalId;
                            decimal actualTotalPrice = CalculateTotalRentalCost() + GetDeliveryCost();

                            if (isEditMode)
                            {
                                rentalId = editingRentalId.Value;

                                string updateQuery = @"UPDATE public.rentals 
                                              SET client_id = @client_id,
                                                  employee_id = @employee_id,
                                                  start_date = @start_date,
                                                  end_date = @end_date,
                                                  status = @status,
                                                  total_price = @total_price
                                              WHERE id = @id";

                                using (var cmd = new NpgsqlCommand(updateQuery, conn, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@id", rentalId);
                                    cmd.Parameters.AddWithValue("@client_id", ((ClientComboItem)cmbClient.SelectedItem).Id);
                                    cmd.Parameters.AddWithValue("@employee_id", ((EmployeeComboItem)cmbEmployee.SelectedItem).Id);
                                    cmd.Parameters.AddWithValue("@start_date", dpStartDate.SelectedDate.Value);
                                    cmd.Parameters.AddWithValue("@end_date", dpEndDate.SelectedDate.Value);
                                    cmd.Parameters.AddWithValue("@status", ((ComboBoxItem)cmbStatus.SelectedItem).Content.ToString());
                                    cmd.Parameters.AddWithValue("@total_price", actualTotalPrice);
                                    cmd.ExecuteNonQuery();
                                }

                                string deleteItemsQuery = "DELETE FROM public.rental_items WHERE rental_id = @rental_id";
                                using (var cmd = new NpgsqlCommand(deleteItemsQuery, conn, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@rental_id", rentalId);
                                    cmd.ExecuteNonQuery();
                                }
                            }
                            else
                            {
                                string insertQuery = @"INSERT INTO public.rentals 
                                              (client_id, employee_id, start_date, end_date, status, total_price)
                                              VALUES (@client_id, @employee_id, @start_date, @end_date, @status, @total_price)
                                              RETURNING id";

                                using (var cmd = new NpgsqlCommand(insertQuery, conn, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@client_id", ((ClientComboItem)cmbClient.SelectedItem).Id);
                                    cmd.Parameters.AddWithValue("@employee_id", ((EmployeeComboItem)cmbEmployee.SelectedItem).Id);
                                    cmd.Parameters.AddWithValue("@start_date", dpStartDate.SelectedDate.Value);
                                    cmd.Parameters.AddWithValue("@end_date", dpEndDate.SelectedDate.Value);
                                    cmd.Parameters.AddWithValue("@status", "активна");
                                    cmd.Parameters.AddWithValue("@total_price", actualTotalPrice);
                                    rentalId = (int)cmd.ExecuteScalar();
                                }
                            }

                            foreach (var item in rentalItems)
                            {
                                string insertItemQuery = @"INSERT INTO public.rental_items 
                                                  (rental_id, tool_id, price_per_day, return_date, is_damaged)
                                                  VALUES (@rental_id, @tool_id, @price_per_day, @return_date, @is_damaged)";

                                using (var cmd = new NpgsqlCommand(insertItemQuery, conn, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@rental_id", rentalId);
                                    cmd.Parameters.AddWithValue("@tool_id", item.ToolId);
                                    cmd.Parameters.AddWithValue("@price_per_day", item.PricePerDay);
                                    cmd.Parameters.AddWithValue("@return_date", item.ReturnDate ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@is_damaged", item.IsDamaged);
                                    cmd.ExecuteNonQuery();
                                }

                                string updateToolQuery = "UPDATE public.tools SET status = 'в аренде' WHERE id = @tool_id";
                                using (var cmd = new NpgsqlCommand(updateToolQuery, conn, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@tool_id", item.ToolId);
                                    cmd.ExecuteNonQuery();
                                }
                            }

                            if (cmbCity.SelectedItem != null)
                            {
                                if (existingDeliveryId.HasValue)
                                {
                                    string updateDeliveryQuery = @"UPDATE public.deliveries 
                                                          SET city_id = @city_id,
                                                              status = @status,
                                                              delivery_address = @delivery_address,
                                                              delivery_date = @delivery_date,
                                                              cost = @cost
                                                          WHERE id = @id";
                                    using (var cmd = new NpgsqlCommand(updateDeliveryQuery, conn, transaction))
                                    {
                                        cmd.Parameters.AddWithValue("@id", existingDeliveryId.Value);
                                        cmd.Parameters.AddWithValue("@city_id", ((CityComboItem)cmbCity.SelectedItem).Id);
                                        cmd.Parameters.AddWithValue("@status", ((ComboBoxItem)cmbDeliveryStatus.SelectedItem)?.Content.ToString() ?? "ожидает");
                                        cmd.Parameters.AddWithValue("@delivery_address", string.IsNullOrEmpty(txtDeliveryAddress.Text) ? DBNull.Value : (object)txtDeliveryAddress.Text);
                                        cmd.Parameters.AddWithValue("@delivery_date", dpDeliveryDate.SelectedDate ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@cost", GetDeliveryCost());
                                        cmd.ExecuteNonQuery();
                                    }
                                }
                                else
                                {
                                    string insertDeliveryQuery = @"INSERT INTO public.deliveries 
                                                          (rental_id, city_id, status, delivery_address, delivery_date, cost)
                                                          VALUES (@rental_id, @city_id, @status, @delivery_address, @delivery_date, @cost)";
                                    using (var cmd = new NpgsqlCommand(insertDeliveryQuery, conn, transaction))
                                    {
                                        cmd.Parameters.AddWithValue("@rental_id", rentalId);
                                        cmd.Parameters.AddWithValue("@city_id", ((CityComboItem)cmbCity.SelectedItem).Id);
                                        cmd.Parameters.AddWithValue("@status", ((ComboBoxItem)cmbDeliveryStatus.SelectedItem)?.Content.ToString() ?? "ожидает");
                                        cmd.Parameters.AddWithValue("@delivery_address", string.IsNullOrEmpty(txtDeliveryAddress.Text) ? DBNull.Value : (object)txtDeliveryAddress.Text);
                                        cmd.Parameters.AddWithValue("@delivery_date", dpDeliveryDate.SelectedDate ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@cost", GetDeliveryCost());
                                        cmd.ExecuteNonQuery();
                                    }
                                }
                            }
                            else
                            {
                                if (existingDeliveryId.HasValue)
                                {
                                    string deleteDeliveryQuery = "DELETE FROM public.deliveries WHERE id = @id";
                                    using (var cmd = new NpgsqlCommand(deleteDeliveryQuery, conn, transaction))
                                    {
                                        cmd.Parameters.AddWithValue("@id", existingDeliveryId.Value);
                                        cmd.ExecuteNonQuery();
                                    }
                                    existingDeliveryId = null;
                                }
                            }

                            transaction.Commit();
                            DialogResult = true;
                            Close();
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении договора: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}