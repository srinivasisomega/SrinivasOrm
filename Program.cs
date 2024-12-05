using SrinivasOrm;
public class Course
{
    [PrimaryKey]
    public int Id { get; set; }

    public string CourseName { get; set; }
}
public class Student
{
    [PrimaryKey]
    public int Id { get; set; }

    [Unique]
    public int Email { get; set; }

    [Nullable]
    public string Name { get; set; }

    [ForeignKey("Course", "Id")]
    public int CourseId { get; set; }
}

class Program
{
    static void Main()
    {
        var connectionString = "Server=COGNINE-L105;Database=bb2;Trusted_Connection=True;Trust Server Certificate=True;";
        var dbContext = new DbContext(connectionString);

        //dbContext.CreateTables(typeof(Student), typeof(Course));
        dbContext.SyncTables(typeof(Student), typeof(Course));

        //dbContext.AddRecord(new Student { Id = 1, Email = "john@example.com", Name = "John", CourseId = 101 });
    }
}

