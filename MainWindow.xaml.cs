using Microsoft.Data.Sqlite;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace JobSearch;

public partial class MainWindow : Window
{
	public MainWindow()
	{
		InitializeComponent();

		using var connection = OpenConnection();
		InitializeDatabase(connection);
		LoadCompanies(connection);
		LoadStatuses(connection);
		LoadRoles(connection);
	}

	public static string DatabasePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "jobsearch.sqlite");
	public static string ConnectionString => $"Data Source={DatabasePath};";

	void LoadCompanies(SqliteConnection conn)
	{
		var companies = new Dictionary<string, int>();

		using (var cmd = new SqliteCommand("SELECT CompanyID, Name FROM Companies", conn))
		using (var reader = cmd.ExecuteReader())
		{
			var dt = new DataTable();
			dt.Load(reader);
			dgCompanies.ItemsSource = dt.DefaultView;

			foreach (var row in dt.Rows.Cast<DataRow>())
			{
				companies[row["Name"].ToString()!] = Convert.ToInt32(row["CompanyID"]);
			}
		}

		// insert into company combobox in alphabetical order
		cmbCompany.Items.Clear();
		foreach (var company in companies.OrderBy(c => c.Key))
		{
			cmbCompany.Items.Add(new CompanyItem(company.Value, company.Key));
		}
	}

	void LoadStatuses(SqliteConnection conn)
	{
		cmbStatus.Items.Clear();

		using var cmd = new SqliteCommand("SELECT StatusID, Name FROM Statuses", conn);
		using var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			var statusItem = new StatusItem(
				Convert.ToInt32(reader["StatusID"]),
				reader["Name"].ToString()!);
			cmbStatus.Items.Add(statusItem);
		}
	}

	void LoadRoles(SqliteConnection conn)
	{
		var query =
			"""
			SELECT r.RoleID, r.RoleName, c.Name AS Company, s.Name AS Status, r.Notes, r.CreatedUTC, r.ModifiedUTC
			FROM Roles r
				JOIN Companies c ON r.CompanyID = c.CompanyID
				JOIN Statuses s ON r.StatusID = s.StatusID
			ORDER BY r.CreatedUTC DESC
			""";
		var dt = new DataTable();

		using (var cmd = new SqliteCommand(query, conn))
		using (var reader = cmd.ExecuteReader())
		{
			dt.Load(reader);
		}

		// create the presentation view of the time - local time
		dt.Columns.Add(new DataColumn("Created", typeof(string)));
		dt.Columns.Add(new DataColumn("Modified", typeof(string)));
		foreach (DataRow row in dt.Rows)
		{
			var createdUTC = DateTime.Parse(row["CreatedUTC"].ToString()!).ToLocalTime();
			var modifiedUTC = DateTime.Parse(row["ModifiedUTC"].ToString()!).ToLocalTime();
			row["Created"] = createdUTC.ToShortDateString() + ' ' + createdUTC.ToShortTimeString();
			row["Modified"] = modifiedUTC.ToShortDateString() + ' ' + modifiedUTC.ToShortTimeString();
		}

		dgRoles.ItemsSource = dt.DefaultView;
	}

	void OnAddCompanyClick(object sender, RoutedEventArgs e)
	{
		var companyName = txtCompanyName.Text.Trim();
		if (string.IsNullOrEmpty(companyName)) return;

		using (var conn = OpenConnection())
		{
			using var cmd = new SqliteCommand("INSERT OR IGNORE INTO Companies(Name) VALUES(@name)", conn);
			cmd.Parameters.AddWithValue("@name", companyName);
			cmd.ExecuteNonQuery();

			LoadCompanies(conn);
		}

		txtCompanyName.Clear();
		tabControl.SelectedIndex = 0;
		SetSelectedIndex<CompanyItem>(cmbCompany, item => item.Name == companyName);
	}

	void OnAddRoleClick(object sender, RoutedEventArgs e)
	{
		if (cmbCompany.SelectedItem == null || cmbStatus.SelectedItem == null) return;

		var roleName = txtRoleName.Text.Trim();
		if (string.IsNullOrEmpty(roleName)) return;

		var notes = txtNotes.Text.Trim();
		var companyId = ((CompanyItem)cmbCompany.SelectedItem).CompanyID;
		var statusId = ((StatusItem)cmbStatus.SelectedItem).StatusID;

		// Get current UTC timestamp in ISO 8601 format
		var nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

		using (var conn = OpenConnection())
		using (var cmd = new SqliteCommand(
			"""
			INSERT INTO Roles
			(CompanyID, StatusID, RoleName, Notes, CreatedUTC, ModifiedUTC)
			VALUES (@companyId, @statusId, @roleName, @notes, @created, @modified)
			""", conn))
		{
			cmd.Parameters.AddWithValue("@companyId", companyId);
			cmd.Parameters.AddWithValue("@statusId", statusId);
			cmd.Parameters.AddWithValue("@roleName", roleName);
			cmd.Parameters.AddWithValue("@notes", notes);
			cmd.Parameters.AddWithValue("@created", nowUtc);
			cmd.Parameters.AddWithValue("@modified", nowUtc);

			cmd.ExecuteNonQuery();

			LoadRoles(conn);
		}

		txtRoleName.Clear();
		txtNotes.Clear();
		cmbStatus.SelectedIndex = 0;
		cmbCompany.SelectedIndex = -1;
	}

	void OnUpdateRoleClick(object sender, RoutedEventArgs e)
	{
		if (dgRoles.SelectedItem == null || cmbStatus.SelectedItem == null)
		{
			return;
		}

		var row = (DataRowView)dgRoles.SelectedItem;
		var roleId = Convert.ToInt32(row["RoleID"]);
		var oldStatus = row["Status"].ToString();
		var oldStatusId = cmbStatus.Items
			.Cast<StatusItem>()
			.First(x => x.Name == oldStatus)
			.StatusID;
		var statusId = ((StatusItem)cmbStatus.SelectedItem).StatusID;
		var notes = txtNotes.Text.Trim();
		var nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

		using (var conn = OpenConnection())
		{
			using (var cmd = new SqliteCommand(
				statusId != oldStatusId
					? "UPDATE Roles SET StatusID=@statusId, Notes=@notes, ModifiedUTC=@modifiedUTC WHERE RoleID=@roleId"
					: "UPDATE Roles SET StatusID=@statusId, Notes=@notes WHERE RoleID=@roleId",
				conn))
			{
				cmd.Parameters.AddWithValue("@statusId", statusId);
				cmd.Parameters.AddWithValue("@notes", notes);
				cmd.Parameters.AddWithValue("@roleId", roleId);
				cmd.Parameters.AddWithValue("@modifiedUTC", nowUtc);
				cmd.ExecuteNonQuery();
			}

			LoadRoles(conn);
		}

		txtRoleName.Clear();
		txtNotes.Clear();
	}

	void OnRolesSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (dgRoles.SelectedItem == null)
		{
			txtRoleName.Text = string.Empty;
			txtNotes.Text = string.Empty;
			cmbCompany.SelectedIndex = -1;
			cmbStatus.SelectedIndex = 0;
			btnUpdateRole.IsEnabled = false;
			btnAddRole.IsEnabled = true;
			return;
		}

		var row = (DataRowView)dgRoles.SelectedItem;
		var roleName = row["RoleName"].ToString()!;
		var notes = row["Notes"].ToString()!;
		var companyName = row["Company"].ToString()!;
		var statusName = row["Status"].ToString()!;

		txtRoleName.Text = roleName;
		txtNotes.Text = notes;
		SetSelectedIndex<CompanyItem>(cmbCompany, item => item.Name == companyName);
		SetSelectedIndex<StatusItem>(cmbStatus, item => item.Name == statusName);
		btnUpdateRole.IsEnabled = true;
		btnAddRole.IsEnabled = false;
	}

	static void InitializeDatabase(SqliteConnection connection)
	{
		InitializeCompaniesTable(connection);
		InitializeRolesTable(connection);
		InitializeStatusesTable(connection);
	}

	static void InitializeCompaniesTable(SqliteConnection connection)
	{
		ExecuteSimpleNonQuery(
			connection,
			"""
			CREATE TABLE IF NOT EXISTS Companies
			(
				CompanyID INTEGER PRIMARY KEY AUTOINCREMENT,
				Name TEXT NOT NULL UNIQUE
			);
			""");
	}

	static void InitializeRolesTable(SqliteConnection connection)
	{
		ExecuteSimpleNonQuery(
			connection,
			"""
			CREATE TABLE IF NOT EXISTS Roles
			(
				RoleID INTEGER PRIMARY KEY AUTOINCREMENT,
				CompanyID INTEGER NOT NULL,
				StatusID INTEGER NOT NULL,
				RoleName TEXT NOT NULL,
				Notes TEXT,
				CreatedUTC DATETIME NOT NULL DEFAULT (datetime('now')),
				ModifiedUTC DATETIME NOT NULL DEFAULT (datetime('now')),
				FOREIGN KEY (CompanyID) REFERENCES Companies(CompanyID),
				FOREIGN KEY (StatusID) REFERENCES Statuses(StatusID)
			);
			""");
	}

	static void InitializeStatusesTable(SqliteConnection connection)
	{
		ExecuteSimpleNonQuery(
			connection,
			"""
			CREATE TABLE IF NOT EXISTS Statuses
			(
				StatusID INTEGER PRIMARY KEY AUTOINCREMENT,
				Name TEXT NOT NULL UNIQUE
			);
			""");

		string[] defaultStatuses = ["Applied", "Acknowledged", "Contacted", "Interviewing", "Offer", "Rejected", "Accepted"];
		var parameters = defaultStatuses
			.Select((value, index) => (parameterName: $"@name" + index.ToString(CultureInfo.InvariantCulture), parameterValue: value))
			.ToDictionary(x => x.parameterName, x => x.parameterValue);
		var valuesText = string.Join(", ", parameters.Keys.Select(name => $"({name})"));

		using var cmd = new SqliteCommand($"INSERT OR IGNORE INTO Statuses(Name) VALUES {valuesText}", connection);
		foreach (var parameter in parameters)
		{
			cmd.Parameters.AddWithValue(parameter.Key, parameter.Value);
		}
		cmd.ExecuteNonQuery();
	}

	static SqliteConnection OpenConnection()
	{
		var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		return connection;
	}

	static void ExecuteSimpleNonQuery(SqliteConnection connection, string query)
	{
		using var cmd = new SqliteCommand(query, connection);
		cmd.ExecuteNonQuery();
	}

	static void SetSelectedIndex<TItem>(ComboBox comboBox, Func<TItem, bool> predicate)
	{
		comboBox.SelectedIndex = comboBox.Items
			.Cast<TItem>()
			.Select((item, index) => (index, item))
			.First(x => predicate(x.item))
			.index;
	}

	record StatusItem(int StatusID, string Name);
	record CompanyItem(int CompanyID, string Name);
}
